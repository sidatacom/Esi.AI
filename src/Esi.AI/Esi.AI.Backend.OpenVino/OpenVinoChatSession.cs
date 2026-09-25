using System.Text.Json;
using System.Text.Json.Nodes;
using Esi.AI.Models;
using OpenVinoSharp;
using OpenVinoSharp.GenAI;

namespace Esi.AI.Backend.OpenVino;

internal sealed class OpenVinoChatSession
{
    private static readonly JsonSerializerOptions ChatJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LLMPipeline? llmPipeline;
    private readonly VLMPipeline? vlmPipeline;

    public OpenVinoChatSession(LLMPipeline pipeline)
    {
        llmPipeline = pipeline;
    }

    public OpenVinoChatSession(VLMPipeline pipeline)
    {
        vlmPipeline = pipeline;
    }

    public OpenVinoGenerationResult Generate(
        IReadOnlyList<OpenAiChatMessage> messages,
        IReadOnlyList<OpenAiToolDefinition>? tools,
        Action<string>? streamer,
        OpenVinoGenerationOptions options,
        Tensor[] images)
    {
        if (messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(messages));

        using var generationConfig = GetGenerationConfig(options);
        using var history = CreateChatHistory(messages, tools, options.ReasoningEffort, images.Length > 0);
        if (llmPipeline is not null)
        {
            using var results = streamer is null
                ? llmPipeline.GenerateWithHistory(history, generationConfig)
                : llmPipeline.GenerateWithHistory(history, generationConfig, text =>
                {
                    streamer(text);
                    return StreamingStatus.Running;
                });
            return CreateGenerationResult(results.GetText(), results.GetPerformanceMetrics());
        }

        if (vlmPipeline is not null)
        {
            using var results = streamer is null
                ? vlmPipeline.GenerateWithHistory(history, images, generationConfig)
                : vlmPipeline.GenerateWithHistory(history, images, generationConfig, text =>
                {
                    streamer(text);
                    return StreamingStatus.Running;
                });
            return CreateGenerationResult(results.GetText(), results.GetPerformanceMetrics());
        }

        throw new InvalidOperationException("No OpenVINO pipeline is available for this chat session.");
    }

    internal static string SerializeChatMessageForHistory(OpenAiChatMessage message)
    {
        var json = JsonSerializer.SerializeToNode(message, ChatJsonOptions)?.AsObject()
            ?? throw new InvalidOperationException("The chat message could not be serialized.");
        if (message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) &&
            json["tool_calls"] is JsonArray toolCalls)
        {
            if (message.Content is null)
                json["content"] = string.Empty;

            foreach (var toolCall in toolCalls)
            {
                if (toolCall?["function"] is not JsonObject function ||
                    function["arguments"] is not JsonValue arguments ||
                    !arguments.TryGetValue<string>(out var argumentsText))
                    continue;

                try
                {
                    if (JsonNode.Parse(argumentsText) is JsonObject argumentsObject)
                        function["arguments"] = argumentsObject;
                }
                catch (JsonException)
                {
                }
            }
        }

        return json.ToJsonString(ChatJsonOptions);
    }

    private static ChatHistory CreateChatHistory(
        IReadOnlyList<OpenAiChatMessage> messages,
        IReadOnlyList<OpenAiToolDefinition>? tools,
        string? reasoningEffort,
        bool hasImages)
    {
        var history = new ChatHistory();
        foreach (var message in messages)
        {
            if (hasImages)
                history.AddMessage(message.Role, GetPlainHistoryContent(message.Content));
            else
                history.PushBackJson(SerializeChatMessageForHistory(message));
        }

        var templateContext = CreateChatTemplateContext(reasoningEffort ?? "high");
        if (templateContext is not null)
        {
            using var context = JsonContainer.FromJsonString(templateContext);
            history.SetExtraContext(context);
        }

        if (tools is { Count: > 0 })
        {
            using var toolDefinitions = JsonContainer.FromJsonString(JsonSerializer.Serialize(tools, ChatJsonOptions));
            history.SetTools(toolDefinitions);
        }

        return history;
    }

    private static string GetPlainHistoryContent(object? content)
    {
        if (content is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return string.Concat(element.EnumerateArray()
                .Where(part => part.TryGetProperty("type", out var type) && type.GetString() == "text")
                .Select(part => part.TryGetProperty("text", out var text) ? text.GetString() : null)
                .Where(text => text is not null));
        }

        return content?.ToString() ?? string.Empty;
    }

    private static string? CreateChatTemplateContext(string? reasoningEffort)
    {
        if (string.IsNullOrWhiteSpace(reasoningEffort))
            return null;
        var normalizedEffort = reasoningEffort.Trim().ToLowerInvariant();
        if (normalizedEffort == "none")
            return "{\"enable_thinking\":false}";
        var templateEffort = normalizedEffort switch
        {
            "low" or "medium" or "xhigh" => normalizedEffort,
            "high" or "max" => "xhigh",
            _ => throw new ArgumentException($"Unsupported reasoning effort '{reasoningEffort}'.", nameof(reasoningEffort))
        };
        return JsonSerializer.Serialize(new { enable_thinking = true, reasoning_effort = templateEffort }, ChatJsonOptions);
    }

    private static OpenVinoGenerationResult CreateGenerationResult(string text, PerformanceMetrics metrics)
    {
        using (metrics)
        {
            var parsed = OpenVinoToolCallParser.Parse(text);
            return new OpenVinoGenerationResult(
                parsed.Text,
                checked((int)metrics.NumGenerationTokens),
                metrics.Throughput.Mean,
                checked((int)metrics.NumInputTokens),
                metrics.TimeToFirstToken.Mean,
                metrics.GenerateDuration.Mean,
                metrics.InferenceDuration.Mean,
                parsed.ToolCalls,
                parsed.ToolCalls.Count > 0 ? "tool_calls" : "stop");
        }
    }

    private GenerationConfig GetGenerationConfig(OpenVinoGenerationOptions options)
    {
        var config = llmPipeline?.GetGenerationConfig() ?? vlmPipeline?.GetGenerationConfig()
            ?? throw new InvalidOperationException("No OpenVINO pipeline is available for this chat session.");
        config.SetMaxNewTokens((ulong)Math.Max(1, options.MaxNewTokens));
        config.SetTemperature(options.Temperature);
        config.SetTopK((ulong)Math.Max(1, options.TopK));
        config.SetTopP(options.TopP);
        config.SetDoSample(options.DoSample);
        config.SetRepetitionPenalty(options.RepetitionPenalty);
        config.SetPresencePenalty(options.PresencePenalty);
        config.SetFrequencyPenalty(options.FrequencyPenalty);
        if (options.Seed is int seed)
            config.SetRngSeed((ulong)seed);
        if (options.StopSequences is { Count: > 0 })
            config.SetStopStrings(options.StopSequences);
        return config;
    }
}

internal sealed record OpenVinoGenerationResult(
    string Text,
    int TokenCount,
    double TokensPerSecond,
    int PromptTokenCount,
    double? TimeToFirstTokenMs,
    double? PrefillDurationMs,
    double? DecodeDurationMs,
    IReadOnlyList<OpenAiToolCall> ToolCalls,
    string FinishReason);

internal sealed record OpenVinoGenerationOptions(
    int MaxNewTokens,
    float Temperature,
    float TopP,
    bool DoSample,
    int TopK,
    float RepetitionPenalty,
    float FrequencyPenalty,
    float PresencePenalty,
    int? Seed,
    IReadOnlyList<string>? StopSequences,
    string? ReasoningEffort);