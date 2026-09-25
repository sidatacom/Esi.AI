using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Esi.AI.Backend.Vllm.Xpu.Grpc;
using Esi.AI.Models;
using Grpc.Core;
using Grpc.Net.Client;
using ModelChatMessage = Esi.AI.Models.ChatMessage;
using GenerateRequest = Esi.AI.Backend.Vllm.Xpu.Grpc.GenerateRequest;
using GenerateResponse = Esi.AI.Backend.Vllm.Xpu.Grpc.GenerateResponse;

namespace Esi.AI.Backend.Vllm.Xpu;

internal sealed class VllmXpuChatSession : IDisposable
{
    private static readonly JsonSerializerOptions ToolCallJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Func<GenerateRequest, CancellationToken, IAsyncEnumerable<GenerateResponse>> generate;
    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly string modelId;
    private int disposed;

    internal VllmXpuChatSession(Inference.InferenceClient client, string modelId)
        : this((request, cancellationToken) => StreamAsync(client, request, cancellationToken), modelId)
    {
    }

    internal VllmXpuChatSession(
        Func<GenerateRequest, CancellationToken, IAsyncEnumerable<GenerateResponse>> generate,
        string modelId)
    {
        this.generate = generate ?? throw new ArgumentNullException(nameof(generate));
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        this.modelId = modelId;
    }

    internal async Task<GenerationResult> GenerateAsync(
        IReadOnlyList<ModelChatMessage> messages,
        ChatGenerationOptions options,
        Func<string, Task>? onToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(disposed != 0, this);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, disposeCancellation.Token);
        var request = VllmXpuGrpcMapper.ToGrpcRequest(messages, modelId, options);
        var started = Stopwatch.StartNew();
        var text = new StringBuilder();
        var tokenCount = 0;
        var promptTokenCount = 0;
        var tokensPerSecond = 0d;
        double? firstTokenMs = null;
        IReadOnlyList<OpenAiToolCall>? toolCalls = null;

        await foreach (var response in generate(request, linkedCancellation.Token)
            .WithCancellation(linkedCancellation.Token).ConfigureAwait(false))
        {
            if (!string.IsNullOrWhiteSpace(response.Error))
                throw new InvalidOperationException($"vLLM generation failed: {response.Error}");
            if (!string.IsNullOrEmpty(response.Delta))
            {
                firstTokenMs ??= started.Elapsed.TotalMilliseconds;
                text.Append(response.Delta);
                if (onToken is not null)
                    await onToken(response.Delta).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(response.ToolCallsJson))
                toolCalls = JsonSerializer.Deserialize<OpenAiToolCall[]>(response.ToolCallsJson, ToolCallJsonOptions);
            tokenCount = Math.Max(tokenCount, (int)response.GeneratedTokens);
            promptTokenCount = Math.Max(promptTokenCount, (int)response.PromptTokens);
            if (response.TokensPerSecond > 0)
                tokensPerSecond = response.TokensPerSecond;
        }

        started.Stop();
        if (string.IsNullOrWhiteSpace(text.ToString()) && toolCalls is not { Count: > 0 })
            throw new InvalidOperationException("vLLM returned an empty answer.");
        if (tokensPerSecond <= 0 && started.Elapsed.TotalSeconds > 0)
            tokensPerSecond = tokenCount / started.Elapsed.TotalSeconds;
        return new GenerationResult(
            text.ToString(),
            tokenCount,
            started.Elapsed,
            tokensPerSecond,
            promptTokenCount,
            toolCalls is { Count: > 0 } ? "tool_calls" : "stop",
            toolCalls,
            firstTokenMs);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private static async IAsyncEnumerable<GenerateResponse> StreamAsync(
        Inference.InferenceClient client,
        GenerateRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = client.Generate(request, cancellationToken: cancellationToken);
        while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
            yield return call.ResponseStream.Current;
    }
}
