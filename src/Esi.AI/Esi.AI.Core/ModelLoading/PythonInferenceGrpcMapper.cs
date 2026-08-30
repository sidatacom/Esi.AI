using Esi.AI.Core.Chat;
using Esi.AI.Core.Grpc;
using Esi.AI.Models;

namespace Esi.AI.Core.ModelLoading;

internal static class PythonInferenceGrpcMapper
{
    public static Esi.AI.Core.Grpc.LoadModelRequest ToGrpcRequest(PythonInferenceLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grpcRequest = new Esi.AI.Core.Grpc.LoadModelRequest
        {
            ModelPath = request.ModelPath,
            Engine = request.Backend == ConfigurationBackend.Vllm ? "vllm" : "sglang",
            MaxModelLen = request.MaxModelLength,
            TensorParallelSize = (uint)request.TensorParallelSize,
            GpuMemoryUtilization = request.GpuMemoryUtilization is int utilization ? utilization / 100f : 0,
            TrustRemoteCode = request.TrustRemoteCode,
            EnforceEager = request.EnforceEager,
            Device = request.Device,
        };
        var devices = request.Devices is { Count: > 0 } ? request.Devices : [request.Device];
        grpcRequest.Devices.AddRange(devices.Where(device => !string.IsNullOrWhiteSpace(device)));
        return grpcRequest;
    }

    public static GenerateRequest ToGrpcRequest(IReadOnlyList<LlamaChatMessage> messages, string modelId)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(messages));
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("A model id is required.", nameof(modelId));

        var request = new GenerateRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ModelId = modelId,
            MaxTokens = 512,
            Temperature = .7f,
            TopP = .9f
        };
        request.Messages.AddRange(messages.Select(message => new ChatMessage
        {
            Role = message.Role,
            Content = message.Content
        }));
        return request;
    }
}