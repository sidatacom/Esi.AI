using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotLLM.Core.Configuration;
using DotLLM.Core.Models;
using DotLLM.Engine;
using DotLLM.Engine.Constraints;
using DotLLM.Models;
using DotLLM.Models.Gguf;
using DotLLM.Tokenizers;
using DotLLM.Tokenizers.ChatTemplates;
using DotLLM.Tokenizers.ToolCallParsers;
using GenerationResult = Esi.AI.Core.Chat.GenerationResult;
using Esi.AI.Models;
using ModelChatMessage = Esi.AI.Models.ChatMessage;
using DotChatMessage = DotLLM.Tokenizers.ChatMessage;
using DotToolCall = DotLLM.Tokenizers.ToolCall;

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
    private IToolCallParser? toolCallParser;
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
        var loadedToolCallParser = GgufChatTemplateFactory.CreateToolCallParser(loaded.Gguf.Metadata, loaded.Model.Config.Architecture);
        lock (sync)
        {
            model = loaded.Model;
            gguf = loaded.Gguf;
            tokenizer = loadedTokenizer;
            chatTemplate = loadedTemplate;
            toolCallParser = loadedToolCallParser;
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
            return new DotLlmInProcessChatSession(model, tokenizer, chatTemplate, toolCallParser);
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
            toolCallParser = null;
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
public sealed class DotLlmInProcessChatSession(
    IModel model,
    DotLLM.Tokenizers.ITokenizer tokenizer,
    JinjaChatTemplate? chatTemplate,
    IToolCallParser? toolCallParser) : IDisposable
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

        var prompt = CreatePrompt(messages, options);
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
            Seed = options.Seed,
            ResponseFormat = CreateToolResponseFormat(options)
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
        var generatedText = responseText.ToString();
        var toolCalls = options.Tools is { Count: > 0 } && toolCallParser is not null
            ? toolCallParser.TryParse(generatedText)
            : null;
        return new GenerationResult(
            toolCalls is { Length: > 0 } ? string.Empty : generatedText,
            generatedTokenCount,
            stopwatch.Elapsed,
            tokensPerSecond,
            promptTokenCount,
            toolCalls is { Length: > 0 } ? "tool_calls" : "stop",
            toolCalls?.Select(call => new OpenAiToolCall(
                call.Id,
                "function",
                new OpenAiToolCallFunction(call.FunctionName, call.Arguments))).ToArray());
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private string CreatePrompt(IReadOnlyList<ModelChatMessage> messages, ChatGenerationOptions options)
    {
        var templateMessages = messages.Select(message => new DotChatMessage
        {
            Role = message.Role,
            Content = message.Content,
            ToolCallId = message.ToolCallId,
            ToolCalls = message.ToolCalls?.Select(call => new DotToolCall(call.Id, call.Function.Name, call.Function.Arguments)).ToArray()
        }).ToArray();
        if (chatTemplate is not null)
            return chatTemplate.Apply(templateMessages, new ChatTemplateOptions
            {
                AddGenerationPrompt = true,
                Tools = CreateToolDefinitions(options)
            });

        return string.Join('\n', messages.Select(message => $"{message.Role}: {message.Content}")) + "\nassistant:";
    }

    private static DotLLM.Tokenizers.ToolDefinition[]? CreateToolDefinitions(ChatGenerationOptions options)
    {
        if (options.Tools is not { Count: > 0 } || IsToolChoiceNone(options.ToolChoice))
            return null;

        return options.Tools.Select(tool => new DotLLM.Tokenizers.ToolDefinition(
            tool.Function.Name,
            tool.Function.Description ?? string.Empty,
            tool.Function.Parameters?.GetRawText() ?? "{}"))
            .ToArray();
    }

    private static ResponseFormat? CreateToolResponseFormat(ChatGenerationOptions options)
    {
        if (options.Tools is not { Count: > 0 } || options.ToolChoice is not JsonElement toolChoice)
            return null;

        if (toolChoice.ValueKind == JsonValueKind.String && toolChoice.GetString() == "required")
            return new ResponseFormat.JsonSchema
            {
                Schema = ToolCallSchemaBuilder.BuildForRequired(CreateToolDefinitions(options)!),
                Name = "tool_call"
            };

        if (toolChoice.ValueKind != JsonValueKind.Object ||
            !toolChoice.TryGetProperty("function", out var function) ||
            !function.TryGetProperty("name", out var nameProperty) ||
            nameProperty.ValueKind != JsonValueKind.String)
            return null;

        var tool = options.Tools.FirstOrDefault(candidate => candidate.Function.Name == nameProperty.GetString());
        return tool is null
            ? throw new ArgumentException($"tool_choice references unknown function '{nameProperty.GetString()}'.", nameof(options))
            : new ResponseFormat.JsonSchema
            {
                Schema = ToolCallSchemaBuilder.BuildForFunction(new DotLLM.Tokenizers.ToolDefinition(
                    tool.Function.Name,
                    tool.Function.Description ?? string.Empty,
                    tool.Function.Parameters?.GetRawText() ?? "{}")),
                Name = "tool_call"
            };
    }

    private static bool IsToolChoiceNone(JsonElement? toolChoice) =>
        toolChoice is JsonElement choice && choice.ValueKind == JsonValueKind.String && choice.GetString() == "none";
}
