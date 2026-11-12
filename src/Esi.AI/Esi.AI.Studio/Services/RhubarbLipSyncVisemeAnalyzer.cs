using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Esi.AI.Models;
using Microsoft.Extensions.Options;

namespace Esi.AI.Studio.Services;

internal sealed record AudioVisemeAnalysisResult(
    int AudioDurationMilliseconds,
    IReadOnlyList<AudioChatVisemeCue> Cues);

internal interface IAudioVisemeAnalyzer
{
    AudioRuntimeComponentStatus GetStatus();
    Task<AudioVisemeAnalysisResult> AnalyzeAsync(ReadOnlyMemory<byte> wavAudio, CancellationToken cancellationToken);
}

internal sealed class RhubarbLipSyncVisemeAnalyzer(
    IOptions<AudioChatOptions> options,
    IHostEnvironment environment,
    ILogger<RhubarbLipSyncVisemeAnalyzer> logger) : IAudioVisemeAnalyzer
{
    public AudioRuntimeComponentStatus GetStatus()
    {
        var settings = options.Value;
        if (settings.VisemeAnalyzerTimeoutSeconds <= 0 ||
            settings.VisemeAnalyzerThreadCount <= 0)
            return new(false, "Viseme analyzer timeout and thread count must be positive.");

        return AudioRuntimePathResolver.ResolveExecutable(
                settings.VisemeAnalyzerExecutablePath,
                environment.ContentRootPath) is null
            ? new(false, "Rhubarb was not found. Configure AudioChat:VisemeAnalyzerExecutablePath.")
            : new(true, null);
    }

    public async Task<AudioVisemeAnalysisResult> AnalyzeAsync(
        ReadOnlyMemory<byte> wavAudio,
        CancellationToken cancellationToken)
    {
        var status = GetStatus();
        if (!status.IsReady)
            throw new InvalidOperationException(status.Issue);

        var audioDurationMilliseconds = WaveAudioMetadata.GetDurationMilliseconds(wavAudio.Span);
        var settings = options.Value;
        var executable = AudioRuntimePathResolver.ResolveExecutable(
            settings.VisemeAnalyzerExecutablePath,
            environment.ContentRootPath)
            ?? throw new InvalidOperationException("The configured Rhubarb executable is no longer available.");
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "esi-ai-audio",
            $"visemes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var audioPath = Path.Combine(temporaryDirectory, "speech.wav");
        var outputPath = Path.Combine(temporaryDirectory, "visemes.json");

        try
        {
            await File.WriteAllBytesAsync(audioPath, wavAudio.ToArray(), cancellationToken).ConfigureAwait(false);

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--recognizer");
            startInfo.ArgumentList.Add("phonetic");
            startInfo.ArgumentList.Add("--exportFormat");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("--machineReadable");
            startInfo.ArgumentList.Add("--threads");
            startInfo.ArgumentList.Add(settings.VisemeAnalyzerThreadCount.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputPath);
            startInfo.ArgumentList.Add(audioPath);

            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(settings.VisemeAnalyzerTimeoutSeconds));

            AudioProcessResult processResult;
            try
            {
                processResult = await AudioProcessRunner.RunAsync(
                    startInfo,
                    null,
                    timeoutCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                timeoutCancellation.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Rhubarb exceeded its {settings.VisemeAnalyzerTimeoutSeconds}-second analysis limit.");
            }

            if (processResult.ExitCode != 0)
                throw new AudioRuntimeException(
                    $"Rhubarb exited with code {processResult.ExitCode}: {processResult.StandardError}");
            if (!File.Exists(outputPath))
                throw new AudioRuntimeException("Rhubarb completed without producing its JSON cue file.");

            var json = await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false);
            var cues = ParseCues(json, audioDurationMilliseconds);
            return new AudioVisemeAnalysisResult(audioDurationMilliseconds, cues);
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Could not remove temporary Rhubarb directory {Path}.", temporaryDirectory);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogWarning(exception, "Could not remove temporary Rhubarb directory {Path}.", temporaryDirectory);
            }
        }
    }

    internal static IReadOnlyList<AudioChatVisemeCue> ParseCues(string json, int audioDurationMilliseconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(audioDurationMilliseconds);

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("mouthCues", out var mouthCues) ||
            mouthCues.ValueKind is not JsonValueKind.Array)
            throw new AudioRuntimeException("Rhubarb output did not contain a mouthCues array.");

        var cues = new List<AudioChatVisemeCue>();
        var previousEndMilliseconds = 0;
        foreach (var mouthCue in mouthCues.EnumerateArray())
        {
            if (!mouthCue.TryGetProperty("start", out var startElement) ||
                !startElement.TryGetDouble(out var startSeconds) ||
                !mouthCue.TryGetProperty("end", out var endElement) ||
                !endElement.TryGetDouble(out var endSeconds) ||
                !mouthCue.TryGetProperty("value", out var valueElement) ||
                valueElement.ValueKind is not JsonValueKind.String)
                throw new AudioRuntimeException("Rhubarb output contained a malformed mouth cue.");

            var startMilliseconds = SecondsToMilliseconds(startSeconds);
            var endMilliseconds = SecondsToMilliseconds(endSeconds);
            if (startMilliseconds < previousEndMilliseconds ||
                endMilliseconds <= startMilliseconds ||
                endMilliseconds > audioDurationMilliseconds)
                throw new AudioRuntimeException("Rhubarb returned a non-monotone cue or a cue outside the WAV duration.");

            var visemeId = MapMouthShape(valueElement.GetString());
            cues.Add(new AudioChatVisemeCue(
                startMilliseconds,
                endMilliseconds - startMilliseconds,
                visemeId,
                1f));
            previousEndMilliseconds = endMilliseconds;
        }

        if (cues.Count == 0)
            throw new AudioRuntimeException("Rhubarb returned no mouth cues for a non-empty WAV file.");

        return cues;
    }

    private static int SecondsToMilliseconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > int.MaxValue / 1000d)
            throw new AudioRuntimeException("Rhubarb returned an invalid cue timestamp.");

        return (int)Math.Floor(seconds * 1000d);
    }

    private static string MapMouthShape(string? shape) => shape switch
    {
        "A" => AudioChatVisemeIds.Closed,
        "B" => AudioChatVisemeIds.TeethClosed,
        "C" => AudioChatVisemeIds.Open,
        "D" => AudioChatVisemeIds.WideOpen,
        "E" => AudioChatVisemeIds.Rounded,
        "F" => AudioChatVisemeIds.Pursed,
        "G" => AudioChatVisemeIds.TeethOnLowerLip,
        "H" => AudioChatVisemeIds.TongueRaised,
        "X" => AudioChatVisemeIds.Neutral,
        _ => throw new AudioRuntimeException($"Rhubarb returned an unsupported mouth shape '{shape}'.")
    };
}

internal static class WaveAudioMetadata
{
    public static int GetDurationMilliseconds(ReadOnlySpan<byte> wave)
    {
        var duration = GetDurationSeconds(wave);
        if (duration <= 0 || duration > int.MaxValue / 1000d)
            throw new AudioRuntimeException("The WAV duration is outside the supported range.");

        return checked((int)Math.Ceiling(duration * 1000d));
    }

    private static double GetDurationSeconds(ReadOnlySpan<byte> wave)
    {
        if (wave.Length < 12 ||
            !wave[..4].SequenceEqual("RIFF"u8) ||
            !wave.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new AudioRuntimeException("Viseme analysis requires a valid RIFF/WAVE file.");

        uint? bytesPerSecond = null;
        uint? dataSize = null;
        var offset = 12;
        while (offset <= wave.Length - 8)
        {
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(wave.Slice(offset + 4, 4));
            var chunkStart = offset + 8;
            var chunkEnd = (long)chunkStart + chunkSize;
            if (chunkEnd > wave.Length)
                throw new AudioRuntimeException("The WAV contains a truncated chunk.");

            if (wave.Slice(offset, 4).SequenceEqual("fmt "u8))
            {
                if (chunkSize < 16)
                    throw new AudioRuntimeException("The WAV format chunk is incomplete.");

                bytesPerSecond = BinaryPrimitives.ReadUInt32LittleEndian(wave.Slice(chunkStart + 8, 4));
            }
            else if (wave.Slice(offset, 4).SequenceEqual("data"u8))
            {
                dataSize = chunkSize;
            }

            var nextChunk = chunkEnd + (chunkSize & 1);
            if (nextChunk > int.MaxValue)
                break;
            offset = (int)nextChunk;
        }

        if (bytesPerSecond is null or 0 || dataSize is null)
            throw new AudioRuntimeException("The WAV is missing a valid format or audio-data chunk.");

        return dataSize.Value / (double)bytesPerSecond.Value;
    }
}
