using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotLLM.Core.Configuration;
using DotLLM.Core.Models;
using DotLLM.Engine;
using DotLLM.Models;
using DotLLM.Models.Gguf;
using DotLLM.Tokenizers;
using DotLLM.Tokenizers.ChatTemplates;
using GenerationResult = Esi.AI.Core.Chat.GenerationResult;
using Esi.AI.Models;
using ModelChatMessage = Esi.AI.Models.ChatMessage;
using DotChatMessage = DotLLM.Tokenizers.ChatMessage;

using System.Text;

namespace Esi.AI.Core.ModelLoading;

/// <summary>
/// Loads and runs a dotLLM GGUF model inside the Studio process.
/// </summary>
public sealed class DotLlmInProcessRuntime : IDisposable
{
    private readonly object sync = new();
    private IModel? model;
    private GgufFile? gguf;
    private DotLLM.Tokenizers.ITokenizer? tokenizer;
    private JinjaChatTemplate? chatTemplate;
    private string? modelPath;
    private string loadLog = string.Empty;

    /// <summary>Returns the current in-process dotLLM status.</summary>
    public ModelLoadStatus GetStatus()
    {
        lock (sync)
        {
            var isLoaded = model is not null && modelPath is not null;
            return new ModelLoadStatus(
                isLoaded ? modelPath : null,
                isLoaded ? "dotLLM" : string.Empty,
                0,
                isLoaded ? checked((uint)model!.Config.MaxSequenceLength) : 0,
                isLoaded ? checked((ulong)Math.Max(0, model!.RepackedWeightBytes)) : 0,
                0,
                [],
                null,
                loadLog,
                new Dictionary<string, float>(),
                isLoaded,
                isLoaded
                    ? [new LoadedModelStatus(modelPath!, ConfigurationBackend.DotLlm, "dotLLM / In-Process", 0, checked((uint)model!.Config.MaxSequenceLength), checked((ulong)Math.Max(0, model!.RepackedWeightBytes)), [], null, loadLog)]
                    : []);
        }
    }

    /// <summary>Loads a GGUF model directly into the Studio process.</summary>
    public async Task LoadAsync(DotLlmLoadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ModelPath))
            throw new ArgumentException("A GGUF model path or dotLLM model id is required.", nameof(request));
        if (!File.Exists(request.ModelPath))
            throw new FileNotFoundException("The dotLLM in-process loader requires a local GGUF file.", request.ModelPath);
        if (!string.Equals(request.Device, "cpu", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("The dotLLM in-process integration currently supports CPU execution only.");

        await StopAsync(cancellationToken).ConfigureAwait(false);
        var loaded = await Task.Run(() => ModelLoader.LoadFromGguf(request.ModelPath, CreateThreading(request.Threads)), cancellationToken).ConfigureAwait(false);
        var loadedTokenizer = GgufBpeTokenizerFactory.Load(loaded.Gguf.Metadata);
        var loadedTemplate = GgufChatTemplateFactory.TryCreate(loaded.Gguf.Metadata, loadedTokenizer);
        lock (sync)
        {
            model = loaded.Model;
            gguf = loaded.Gguf;
            tokenizer = loadedTokenizer;
            chatTemplate = loadedTemplate;
            modelPath = request.ModelPath;
            loadLog = $"Loaded dotLLM in-process on {request.Device}.";
        }
    }

    /// <summary>Creates a chat session backed by the in-process model.</summary>
    public DotLlmInProcessChatSession CreateChatSession()
    {
        lock (sync)
        {
            if (model is null || tokenizer is null)
                throw new InvalidOperationException("No in-process dotLLM model is loaded.");
            return new DotLlmInProcessChatSession(model, tokenizer, chatTemplate);
        }
    }

    /// <summary>Unloads the in-process dotLLM model.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IModel? activeModel;
        GgufFile? activeGguf;
        lock (sync)
        {
            activeModel = model;
            activeGguf = gguf;
            model = null;
            gguf = null;
            tokenizer = null;
            chatTemplate = null;
            modelPath = null;
            loadLog = "dotLLM unloaded.";
        }
        activeModel?.Dispose();
        activeGguf?.Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private static ThreadingConfig CreateThreading(int? threads) => threads is > 0 ? new ThreadingConfig(threads.Value) : ThreadingConfig.Auto;
}

/// <summary>Generates chat responses using an in-process dotLLM model.</summary>
public sealed class DotLlmInProcessChatSession(IModel model, DotLLM.Tokenizers.ITokenizer tokenizer, JinjaChatTemplate? chatTemplate) : IDisposable
{
    /// <summary>Generates a response for the supplied chat messages.</summary>
    public Task<GenerationResult> GenerateWithStatsAsync(IReadOnlyList<ModelChatMessage> messages, CancellationToken cancellationToken = default) =>
        GenerateWithStatsAsync(messages, null, new ChatGenerationOptions(), cancellationToken);

    public Task<GenerationResult> GenerateWithStatsAsync(
        IReadOnlyList<ModelChatMessage> messages,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken = default) =>
        GenerateWithStatsAsync(messages, onDelta, new ChatGenerationOptions(), cancellationToken);

    /// <summary>Generates a response while forwarding each streamed text fragment.</summary>
    public async Task<GenerationResult> GenerateWithStatsAsync(
        IReadOnlyList<ModelChatMessage> messages,
        Func<string, Task>? onDelta,
        ChatGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(messages));

        var prompt = CreatePrompt(messages);
        var generator = new TextGenerator(model, tokenizer);
        var inferenceOptions = new InferenceOptions
        {
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK,
            MinP = options.MinP,
            RepetitionPenalty = options.RepetitionPenalty,
            StopSequences = options.StopSequences ?? [],
            Seed = options.Seed
        };
        var stopwatch = Stopwatch.StartNew();
        var responseText = new StringBuilder();
        var generatedTokenCount = 0;
        await foreach (var token in generator.GenerateStreamingTokensAsync(prompt, inferenceOptions, cancellationToken).ConfigureAwait(false))
        {
            responseText.Append(token.Text);
            generatedTokenCount++;
            if (!string.IsNullOrEmpty(token.Text) && onDelta is not null)
                await onDelta(token.Text).ConfigureAwait(false);
        }
        stopwatch.Stop();
        var tokensPerSecond = stopwatch.Elapsed.TotalSeconds > 0 ? generatedTokenCount / stopwatch.Elapsed.TotalSeconds : 0;
        if (string.IsNullOrWhiteSpace(responseText.ToString()))
            throw new InvalidOperationException("dotLLM returned an empty answer.");
        var promptTokenCount = tokenizer.Encode(prompt).Length;
        return new GenerationResult(responseText.ToString(), generatedTokenCount, stopwatch.Elapsed, tokensPerSecond, promptTokenCount);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private string CreatePrompt(IReadOnlyList<ModelChatMessage> messages)
    {
        var templateMessages = messages.Select(message => new DotChatMessage { Role = message.Role, Content = message.Content }).ToArray();
        if (chatTemplate is not null)
            return chatTemplate.Apply(templateMessages, new ChatTemplateOptions { AddGenerationPrompt = true });

        return string.Join('\n', messages.Select(message => $"{message.Role}: {message.Content}")) + "\nassistant:";
    }
}
