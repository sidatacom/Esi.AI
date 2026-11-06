using System.Text.Json;
using Esi.AI.Models;
using Google.Protobuf;
using GrpcProtocol = Esi.AI.Core.Grpc;

namespace Esi.AI.Backend.Vllm.Cuda12;

internal static class VllmCuda12GrpcMapper
{
    public static GrpcProtocol.LoadModelRequest ToGrpcRequest(PythonInferenceLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grpcRequest = new GrpcProtocol.LoadModelRequest
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
        };
        var devices = request.Devices is { Count: > 0 } ? request.Devices : [request.Device];
        grpcRequest.Devices.AddRange(devices.Where(device => !string.IsNullOrWhiteSpace(device)).Select(device => device.Trim()));
        return grpcRequest;
    }

    public static GrpcProtocol.GenerateRequest ToGrpcRequest(
        OpenAiBackendChatRequest request,
        string modelId,
        string? requestId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Messages);
        if (request.Messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var options = request.Options;
        var grpcRequest = new GrpcProtocol.GenerateRequest
        {
            RequestId = requestId ?? Guid.NewGuid().ToString("N"),
            ModelId = modelId,
            MaxTokens = (uint)Math.Max(1, options.MaxTokens),
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK,
            MinP = options.MinP,
            RepetitionPenalty = options.RepetitionPenalty,
            Seed = options.Seed ?? 0,
            StructuredOutputJsonSchema = options.StructuredOutputJsonSchema ?? string.Empty,
            ReasoningEffort = options.ReasoningEffort ?? string.Empty,
        };
        grpcRequest.StopSequences.AddRange(options.StopSequences ?? []);

        var tools = options.Tools ?? request.Tools;
        if (tools is { Count: > 0 })
        {
            grpcRequest.Tools.AddRange(tools.Select(tool => new GrpcProtocol.ToolDefinition
            {
                Type = tool.Type,
                Name = tool.Function.Name,
                Description = tool.Function.Description ?? string.Empty,
                ParametersJson = tool.Function.Parameters?.GetRawText() ?? "{}",
            }));
        }

        if (options.ToolChoice is JsonElement toolChoice && toolChoice.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            grpcRequest.ToolChoiceJson = toolChoice.GetRawText();

        grpcRequest.Messages.AddRange(request.Messages.Select(message =>
        {
            var grpcMessage = new GrpcProtocol.ChatMessage
            {
                Role = message.Role,
                Content = message.Content,
                ToolCallsJson = message.ToolCalls is { Count: > 0 } ? JsonSerializer.Serialize(message.ToolCalls) : string.Empty,
                ToolCallId = message.ToolCallId ?? string.Empty,
            };
            if (message.Images is not null)
                grpcMessage.Images.AddRange(message.Images.Select(image => new GrpcProtocol.ChatImage
                {
                    MediaType = image.MediaType,
                    Data = ByteString.CopyFrom(image.Data)
                }));
            if (message.ContentParts is not null)
            {
                foreach (var part in message.ContentParts)
                {
                    if (part.Text is string text && part.ImageIndex is null)
                        grpcMessage.ContentParts.Add(new GrpcProtocol.ChatMessageContentPart { Text = text });
                    else if (part.ImageIndex is int imageIndex && part.Text is null &&
                             imageIndex >= 0 && imageIndex < grpcMessage.Images.Count)
                        grpcMessage.ContentParts.Add(new GrpcProtocol.ChatMessageContentPart { ImageIndex = (uint)imageIndex });
                    else
                        throw new ArgumentException("Chat content parts must contain text or a valid image index.", nameof(request));
                }
            }
            return grpcMessage;
        }));
        return grpcRequest;
    }
}