using System.Text.Json;
using Esi.AI.Backend.Vllm.Xpu.Grpc;
using Esi.AI.Models;
using ModelChatMessage = Esi.AI.Models.ChatMessage;
using GrpcLoadModelRequest = Esi.AI.Backend.Vllm.Xpu.Grpc.LoadModelRequest;

namespace Esi.AI.Backend.Vllm.Xpu;

internal static class VllmXpuGrpcMapper
{
    internal static GrpcLoadModelRequest ToGrpcRequest(PythonInferenceLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new GrpcLoadModelRequest
        {
            ModelPath = request.ModelPath,
            Engine = "vllm",
            MaxModelLen = request.MaxModelLength,
            TensorParallelSize = (uint)request.TensorParallelSize,
            GpuMemoryUtilization = request.GpuMemoryUtilization is int utilization ? utilization / 100f : 0,
            TrustRemoteCode = request.TrustRemoteCode,
            EnforceEager = request.EnforceEager,
            Device = request.Device,
            Quantization = request.Quantization,
            Dtype = request.DType,
            KvCacheDtype = request.KvCacheDType,
            SpeculativeConfigJson = request.SpeculativeConfigJson,
            MaxNumSeqs = request.MaxNumSeqs,
            MaxNumBatchedTokens = request.MaxNumBatchedTokens,
            EnablePrefixCaching = request.EnablePrefixCaching,
            EnableXpuGraph = request.EnableXpuGraph,
            EnableBf16MtpDraft = request.EnableBf16MtpDraft
        };
        result.Devices.AddRange(request.Devices ?? [request.Device]);
        return result;
    }

    internal static GenerateRequest ToGrpcRequest(
        IReadOnlyList<ModelChatMessage> messages,
        string modelId,
        ChatGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(options);
        if (messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(messages));
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var request = new GenerateRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ModelId = modelId,
            MaxTokens = (uint)Math.Max(1, options.MaxTokens),
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK,
            MinP = options.MinP,
            RepetitionPenalty = options.RepetitionPenalty,
            Seed = options.Seed ?? 0
        };
        request.StopSequences.AddRange(options.StopSequences ?? []);
        if (options.Tools is { Count: > 0 })
        {
            request.Tools.AddRange(options.Tools.Select(tool => new ToolDefinition
            {
                Type = tool.Type,
                Name = tool.Function.Name,
                Description = tool.Function.Description ?? string.Empty,
                ParametersJson = tool.Function.Parameters?.GetRawText() ?? "{}"
            }));
        }
        if (options.ToolChoice is JsonElement toolChoice && toolChoice.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            request.ToolChoiceJson = toolChoice.GetRawText();
        request.Messages.AddRange(messages.Select(message => new Grpc.ChatMessage
        {
            Role = message.Role,
            Content = message.Content,
            ToolCallsJson = message.ToolCalls is { Count: > 0 } ? JsonSerializer.Serialize(message.ToolCalls) : string.Empty,
            ToolCallId = message.ToolCallId ?? string.Empty
        }));
        return request;
    }
}
