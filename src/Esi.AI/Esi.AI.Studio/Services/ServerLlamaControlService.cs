using Esi.AI.Llm.ModelLoading;
using Esi.AI.Studio.Client.Services;
using ClientLoadModelRequest = Esi.AI.Studio.Client.Services.LoadModelRequest;
using ClientModelLoadStatus = Esi.AI.Studio.Client.Services.ModelLoadStatus;

namespace Esi.AI.Studio.Services;

public sealed class ServerLlamaControlService(LlamaModelLoader modelLoader) : ILlamaControlService
{
    public Task<ClientModelLoadStatus> GetModelStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ToClientStatus(modelLoader.GetStatus()));

    public async Task<ClientModelLoadStatus> LoadModelAsync(ClientLoadModelRequest request, CancellationToken cancellationToken = default)
    {
        var advanced = request.Advanced;
        await modelLoader.LoadAsync(request.ModelPath, request.Backend, request.GpuLayerCount, request.ContextSize,
            request.VulkanDeviceWeights,
            new LlamaLoadOptions(advanced.MainGpu, advanced.SeqMax, advanced.RecurrentRollbackSnapshots, advanced.UseMemorymap,
                advanced.UseDirectIO, advanced.UseMemoryLock, advanced.Threads, advanced.BatchThreads, advanced.BatchSize,
                advanced.UBatchSize, advanced.Embeddings, advanced.NoKqvOffload, advanced.FlashAttention, advanced.VocabOnly,
                advanced.OpOffload, advanced.SwaFull, advanced.KVUnified, advanced.RopeFrequencyBase, advanced.RopeFrequencyScale,
                advanced.YarnExtrapolationFactor, advanced.YarnAttentionFactor, advanced.YarnBetaFast, advanced.YarnBetaSlow,
                advanced.YarnOriginalContext), cancellationToken);
        return ToClientStatus(modelLoader.GetStatus());
    }

    public async Task<ClientModelLoadStatus> UnloadModelAsync(CancellationToken cancellationToken = default)
    {
        await modelLoader.StopAsync(cancellationToken);
        return ToClientStatus(modelLoader.GetStatus());
    }

    public async Task<ClientModelLoadStatus> UnloadModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        await modelLoader.UnloadAsync(modelPath, cancellationToken);
        return ToClientStatus(modelLoader.GetStatus());
    }

    private static ClientModelLoadStatus ToClientStatus(Esi.AI.Llm.ModelLoading.ModelLoadStatus status) =>
        new(status.ModelPath, status.Backend, status.GpuLayerCount, status.ContextSize, status.ModelSizeInBytes,
            status.FoundVulkanGpuCount,
            status.VulkanDevices.Select(device => new Esi.AI.Studio.Client.Services.VulkanDeviceStatus(device.Name, device.Description, device.AssignedLayerCount, device.ModelBufferMiB)).ToArray(),
            status.CpuModelBufferMiB, status.LoadLog, status.VulkanDeviceWeights, status.IsModelLoaded,
            status.LoadedModels.Select(ToClientLoadedModelStatus).ToArray());

    private static Esi.AI.Studio.Client.Services.LoadedModelStatus ToClientLoadedModelStatus(Esi.AI.Llm.ModelLoading.LoadedModelStatus model) =>
        new(model.ModelPath, model.Backend, model.GpuLayerCount, model.ContextSize, model.ModelSizeInBytes,
            model.VulkanDevices.Select(device => new Esi.AI.Studio.Client.Services.VulkanDeviceStatus(device.Name, device.Description, device.AssignedLayerCount, device.ModelBufferMiB)).ToArray(),
            model.CpuModelBufferMiB);
}