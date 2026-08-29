using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotLLM.Core.Configuration;
using DotLLM.Core.Models;
using DotLLM.Engine;
using DotLLM.Models;
using DotLLM.Models.Gguf;
using DotLLM.Tokenizers;
using DotLLM.Tokenizers.ChatTemplates;
using Esi.AI.Core.Chat;
using Esi.AI.Models;
using DotChatMessage = DotLLM.Tokenizers.ChatMessage;

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
    public async Task<LlamaGenerationResult> GenerateWithStatsAsync(IReadOnlyList<LlamaChatMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(messages));

        var prompt = CreatePrompt(messages);
        var generator = new TextGenerator(model, tokenizer);
        var options = new InferenceOptions { MaxTokens = 128, Temperature = 0.7f, TopP = 0.95f, TopK = 40 };
        var stopwatch = Stopwatch.StartNew();
        var response = await Task.Run(() => generator.Generate(prompt, options), cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        var tokensPerSecond = stopwatch.Elapsed.TotalSeconds > 0 ? response.GeneratedTokenCount / stopwatch.Elapsed.TotalSeconds : 0;
        if (string.IsNullOrWhiteSpace(response.Text))
            throw new InvalidOperationException("dotLLM returned an empty answer.");
        return new LlamaGenerationResult(response.Text, response.GeneratedTokenCount, stopwatch.Elapsed, tokensPerSecond);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private string CreatePrompt(IReadOnlyList<LlamaChatMessage> messages)
    {
        var templateMessages = messages.Select(message => new DotChatMessage { Role = message.Role, Content = message.Content }).ToArray();
        if (chatTemplate is not null)
            return chatTemplate.Apply(templateMessages, new ChatTemplateOptions { AddGenerationPrompt = true });

        return string.Join('\n', messages.Select(message => $"{message.Role}: {message.Content}")) + "\nassistant:";
    }
}
