using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Esi.AI.Models;
using Esi.AI.Studio.Contracts;
using Esi.AI.Studio.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Esi.AI.Studio.Services;

/// <summary>Manages continuous microphone sessions and streams recognized and synthesized turns.</summary>
public interface IAudioChatService
{
    AudioChatRuntimeReadiness Readiness();
    Task<AudioChatSessionInfo> StartAsync(string connectionId, AudioChatStartRequest request, CancellationToken cancellationToken);
    Task<AudioChatChunkReceipt> WriteAudioAsync(string connectionId, AudioChatAudioChunkRequest request, CancellationToken cancellationToken);
    Task<bool> PlaybackCompletedAsync(string connectionId, Guid sessionId, Guid turnId);
    Task StopAsync(string connectionId, Guid sessionId, CancellationToken cancellationToken);
    Task DisconnectAsync(string connectionId);
}

internal sealed class AudioChatService(
    IOptions<AudioChatOptions> options,
    IAudioTranscriber transcriber,
    IAudioSpeechSynthesizer speechSynthesizer,
    IServiceScopeFactory scopeFactory,
    IHubContext<DataHub> hubContext,
    ILogger<AudioChatService> logger) : IAudioChatService
{
    private readonly ConcurrentDictionary<string, AudioSession> sessions = new(StringComparer.Ordinal);

    public AudioChatRuntimeReadiness Readiness()
    {
        var settings = options.Value;
        var whisper = transcriber.GetStatus();
        var piper = speechSynthesizer.GetStatus();
        var settingsIssue = settings.SampleRate == 16000 &&
            settings.MaximumChunkBytes > 0 &&
            settings.MaximumChunkBytes % sizeof(short) == 0 &&
            settings.BufferedChunkCapacity > 0 &&
            settings.VoiceThresholdRms > 0 &&
            settings.EndOfSpeechSilenceMilliseconds > 0 &&
            settings.PreRollMilliseconds >= 0 &&
            settings.MinimumSpeechMilliseconds >= 0 &&
            settings.MaximumUtteranceSeconds > 0 &&
            settings.PlaybackAcknowledgementTimeoutSeconds > 0
                ? null
                : "AudioChat settings are invalid. Use 16 kHz PCM16 mono and positive buffer/VAD limits.";

        return new AudioChatRuntimeReadiness(
            whisper.IsReady && piper.IsReady && settingsIssue is null,
            whisper.IsReady,
            whisper.Issue,
            piper.IsReady,
            piper.Issue,
            settingsIssue,
            settings.SampleRate,
            settings.MaximumChunkBytes);
    }

    public async Task<AudioChatSessionInfo> StartAsync(
        string connectionId,
        AudioChatStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.ChatId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ModelPath) ||
            string.IsNullOrWhiteSpace(request.Backend))
            throw new ArgumentException("A chat, model path, and backend are required.", nameof(request));

        var readiness = Readiness();
        if (!readiness.IsReady)
        {
            var issues = new[] { readiness.WhisperIssue, readiness.PiperIssue, readiness.ConfigurationIssue }
                .Where(issue => !string.IsNullOrWhiteSpace(issue));
            throw new InvalidOperationException(string.Join(" ", issues));
        }

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
            if (await dataService.Chat_ReadAsync(request.ChatId, cancellationToken).ConfigureAwait(false) is null)
                throw new InvalidOperationException("The selected chat does not exist.");
        }

        var session = new AudioSession(
            connectionId,
            request,
            options.Value,
            cancellationToken);
        if (!sessions.TryAdd(connectionId, session))
        {
            session.Dispose();
            throw new InvalidOperationException("An audio-chat session is already active for this SignalR connection.");
        }

        session.Worker = Task.Run(() => ProcessSessionAsync(session));
        return new AudioChatSessionInfo(session.Id, options.Value.SampleRate, "pcm_s16le_mono", options.Value.MaximumChunkBytes);
    }

    public async Task<AudioChatChunkReceipt> WriteAudioAsync(
        string connectionId,
        AudioChatAudioChunkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Pcm16Mono is null ||
            request.Pcm16Mono.Length == 0 ||
            request.Pcm16Mono.Length % sizeof(short) != 0 ||
            request.Pcm16Mono.Length > options.Value.MaximumChunkBytes)
            throw new ArgumentException(
                $"Audio chunks must contain 1 to {options.Value.MaximumChunkBytes} bytes of complete PCM16 samples.",
                nameof(request));

        var session = GetSession(connectionId, request.SessionId);
        if (session.IsBusy)
            return new AudioChatChunkReceipt(false, "assistant-speaking");

        await session.Audio.Writer.WriteAsync(request.Pcm16Mono.ToArray(), cancellationToken).ConfigureAwait(false);
        return new AudioChatChunkReceipt(true);
    }

    public Task<bool> PlaybackCompletedAsync(string connectionId, Guid sessionId, Guid turnId)
    {
        var session = GetSessionOrNull(connectionId, sessionId);
        return Task.FromResult(session?.CompletePlayback(turnId) ?? false);
    }

    public async Task StopAsync(string connectionId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(connectionId, out var session))
            return;
        if (session.Id != sessionId)
            throw new InvalidOperationException("The audio-chat session ID does not match the active SignalR session.");

        session.Audio.Writer.TryComplete();
        await session.Worker.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(string connectionId)
    {
        if (!sessions.TryRemove(connectionId, out var session))
            return;

        session.Cancel();
        session.Audio.Writer.TryComplete();
        try
        {
            await session.Worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessSessionAsync(AudioSession session)
    {
        try
        {
            await SendStateAsync(session, "listening", session.Cancellation.Token).ConfigureAwait(false);
            await foreach (var chunk in session.Audio.Reader.ReadAllAsync(session.Cancellation.Token).ConfigureAwait(false))
            {
                var utterance = session.VoiceActivityDetector.ProcessChunk(chunk);
                if (utterance is null)
                    continue;

                await ProcessUtteranceAsync(session, utterance, session.Cancellation.Token).ConfigureAwait(false);
                while (session.Audio.Reader.TryRead(out _))
                {
                }
            }

            var finalUtterance = session.VoiceActivityDetector.Flush();
            if (finalUtterance is not null)
                await ProcessUtteranceAsync(session, finalUtterance, session.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Audio-chat session {SessionId} failed.", session.Id);
            await SendErrorAsync(session, exception).ConfigureAwait(false);
        }
        finally
        {
            ((ICollection<KeyValuePair<string, AudioSession>>)sessions)
                .Remove(new KeyValuePair<string, AudioSession>(session.ConnectionId, session));
            try
            {
                if (!session.Cancellation.IsCancellationRequested)
                    await SendStateAsync(session, "stopped", CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not report stopped audio-chat session {SessionId}.", session.Id);
            }
            finally
            {
                session.Dispose();
            }
        }
    }

    private async Task ProcessUtteranceAsync(AudioSession session, byte[] pcm16Mono, CancellationToken cancellationToken)
    {
        session.SetBusy(true);
        var turnId = Guid.NewGuid();
        try
        {
            await SendStateAsync(session, "transcribing", cancellationToken).ConfigureAwait(false);
            var transcript = await transcriber.TranscribeAsync(pcm16Mono, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(transcript))
                return;

            await hubContext.Clients.Client(session.ConnectionId)
                .SendAsync("AudioChat_Transcript", new AudioChatTranscriptUpdate(session.Id, turnId, transcript), cancellationToken)
                .ConfigureAwait(false);
            await SendStateAsync(session, "responding", cancellationToken).ConfigureAwait(false);

            await using var scope = scopeFactory.CreateAsyncScope();
            var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
            var chatRequest = new ChatExchangeRequest(
                transcript,
                session.Request.ModelPath,
                session.Request.Backend,
                BackendVariantId: session.Request.BackendVariantId);
            var pendingSpeech = new StringBuilder();
            PersistedChat? completedChat = null;
            var audioSequence = 0;
            TaskCompletionSource? playbackCompletion = null;

            await foreach (var update in dataService.Chat_UpdateStreamAsync(
                session.Request.ChatId,
                chatRequest,
                cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (update.IsCompleted && update.Chat is not null)
                {
                    completedChat = update.Chat;
                    continue;
                }

                if (string.IsNullOrEmpty(update.Delta))
                    continue;

                await hubContext.Clients.Client(session.ConnectionId)
                    .SendAsync("AudioChat_Text", new AudioChatTextUpdate(session.Id, turnId, update.Delta), cancellationToken)
                    .ConfigureAwait(false);
                pendingSpeech.Append(update.Delta);

                while (TryTakeSpeechSegment(pendingSpeech, out var speechText))
                {
                    if (playbackCompletion is null)
                    {
                        playbackCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        session.SetPlayback(turnId, playbackCompletion);
                    }
                    await SynthesizeAndSendAsync(
                        session,
                        turnId,
                        speechText,
                        audioSequence++,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            var finalSpeech = pendingSpeech.ToString().Trim();
            if (finalSpeech.Length > 0)
            {
                if (playbackCompletion is null)
                {
                    playbackCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    session.SetPlayback(turnId, playbackCompletion);
                }
                await SynthesizeAndSendAsync(
                    session,
                    turnId,
                    finalSpeech,
                    audioSequence,
                    cancellationToken).ConfigureAwait(false);
            }

            if (completedChat is null)
                throw new InvalidOperationException("The chat stream ended without a completed response.");

            if (playbackCompletion is not null)
            {
                await SendStateAsync(session, "awaiting-playback", cancellationToken).ConfigureAwait(false);
                await playbackCompletion.Task.WaitAsync(
                    TimeSpan.FromSeconds(options.Value.PlaybackAcknowledgementTimeoutSeconds),
                    cancellationToken).ConfigureAwait(false);
                session.ClearPlayback(turnId);
            }

            await hubContext.Clients.Client(session.ConnectionId)
                .SendAsync("AudioChat_TurnCompleted", new AudioChatTurnCompleted(session.Id, turnId, completedChat), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            session.ClearPlayback(turnId);
            session.SetBusy(false);
            await SendStateAsync(session, "listening", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SynthesizeAndSendAsync(
        AudioSession session,
        Guid turnId,
        string text,
        int sequence,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        await SendStateAsync(session, "speaking", cancellationToken).ConfigureAwait(false);
        var audio = await speechSynthesizer.SynthesizeAsync(text, cancellationToken).ConfigureAwait(false);
        await hubContext.Clients.Client(session.ConnectionId)
            .SendAsync(
                "AudioChat_AudioChunk",
                new AudioChatAudioChunk(session.Id, turnId, sequence, "audio/wav", audio),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendStateAsync(AudioSession session, string state, CancellationToken cancellationToken) =>
        await hubContext.Clients.Client(session.ConnectionId)
            .SendAsync("AudioChat_State", new AudioChatStateUpdate(session.Id, state), cancellationToken)
            .ConfigureAwait(false);

    private async Task SendErrorAsync(AudioSession session, Exception exception)
    {
        try
        {
            await hubContext.Clients.Client(session.ConnectionId)
                .SendAsync("AudioChat_Error", new AudioChatError(session.Id, exception.Message), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception sendException)
        {
            logger.LogError(sendException, "Could not report audio-chat failure for session {SessionId}.", session.Id);
        }
    }

    private AudioSession GetSession(string connectionId, Guid sessionId) =>
        GetSessionOrNull(connectionId, sessionId)
        ?? throw new InvalidOperationException("The audio-chat session is not active for this SignalR connection.");

    private AudioSession? GetSessionOrNull(string connectionId, Guid sessionId) =>
        sessions.TryGetValue(connectionId, out var session) && session.Id == sessionId
            ? session
            : null;

    private static bool TryTakeSpeechSegment(StringBuilder pendingSpeech, out string speechText)
    {
        for (var index = 0; index < pendingSpeech.Length; index++)
        {
            if (pendingSpeech[index] is not ('.' or '!' or '?' or '\n'))
                continue;

            speechText = pendingSpeech.ToString(0, index + 1).Trim();
            pendingSpeech.Remove(0, index + 1);
            return speechText.Length > 0;
        }

        speechText = string.Empty;
        return false;
    }

    private sealed class AudioSession : IDisposable
    {
        private int disposed;
        private readonly object playbackSync = new();
        private TaskCompletionSource? playbackCompletion;
        private Guid currentPlaybackTurn;
        private int busy;

        public AudioSession(
            string connectionId,
            AudioChatStartRequest request,
            AudioChatOptions options,
            CancellationToken connectionAborted)
        {
            ConnectionId = connectionId;
            Request = request;
            Id = Guid.NewGuid();
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(connectionAborted);
            Audio = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(options.BufferedChunkCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
            VoiceActivityDetector = new AudioVoiceActivityDetector(options);
        }

        public string ConnectionId { get; }
        public AudioChatStartRequest Request { get; }
        public Guid Id { get; }
        public CancellationTokenSource Cancellation { get; }
        public Channel<byte[]> Audio { get; }
        public AudioVoiceActivityDetector VoiceActivityDetector { get; }
        public Task Worker { get; set; } = Task.CompletedTask;
        public bool IsBusy => Volatile.Read(ref busy) != 0;

        public void SetBusy(bool value) => Volatile.Write(ref busy, value ? 1 : 0);

        public void SetPlayback(Guid turnId, TaskCompletionSource completion)
        {
            lock (playbackSync)
            {
                currentPlaybackTurn = turnId;
                playbackCompletion = completion;
            }
        }

        public bool CompletePlayback(Guid turnId)
        {
            lock (playbackSync)
            {
                if (currentPlaybackTurn != turnId)
                    return false;

                return playbackCompletion?.TrySetResult() ?? false;
            }
        }

        public void ClearPlayback(Guid turnId)
        {
            lock (playbackSync)
            {
                if (currentPlaybackTurn == turnId)
                {
                    currentPlaybackTurn = Guid.Empty;
                    playbackCompletion = null;
                }
            }
        }

        public void Cancel()
        {
            if (Volatile.Read(ref disposed) != 0)
                return;

            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                VoiceActivityDetector.Dispose();
                Cancellation.Dispose();
            }
        }
    }
}
