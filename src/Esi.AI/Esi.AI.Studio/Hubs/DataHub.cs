using Esi.AI.Studio.Client.Services;
using Esi.AI.Llm.ModelLoading;
using Microsoft.AspNetCore.SignalR;
using ClientLoadModelRequest = Esi.AI.Studio.Client.Services.LoadModelRequest;
using ClientModelLoadStatus = Esi.AI.Studio.Client.Services.ModelLoadStatus;

namespace Esi.AI.Studio.Hubs;

public sealed class DataHub(IDataService dataService, LlamaModelLoader modelLoader) : Hub
{
    public Task<LlamaSettings?> GetLlamaSettings() => dataService.GetLlamaSettingsAsync(Context.ConnectionAborted);

    public Task SaveLlamaSettings(LlamaSettings settings) => dataService.SaveLlamaSettingsAsync(settings, Context.ConnectionAborted);

    public Task<IReadOnlyList<LlamaModel>> GetLlamaModels() => dataService.GetLlamaModelsAsync(Context.ConnectionAborted);

    public Task<IReadOnlyList<LlamaModel>> ScanLlamaModels() => dataService.ScanLlamaModelsAsync(Context.ConnectionAborted);

    public Task SyncLlamaModels(IReadOnlyList<LlamaModel> models) => dataService.SyncLlamaModelsAsync(models, Context.ConnectionAborted);

    public Task<IReadOnlyList<LlamaConfigurationProfile>> GetLlamaConfigurationProfiles() =>
        dataService.GetLlamaConfigurationProfilesAsync(Context.ConnectionAborted);

    public Task<LlamaConfigurationProfile?> GetLlamaConfigurationProfile(Guid id) =>
        dataService.GetLlamaConfigurationProfileAsync(id, Context.ConnectionAborted);

    public Task<LlamaConfigurationProfile> SaveLlamaConfigurationProfile(LlamaConfigurationProfile profile) =>
        dataService.SaveLlamaConfigurationProfileAsync(profile, Context.ConnectionAborted);

    public Task DeleteLlamaConfigurationProfile(Guid id) =>
        dataService.DeleteLlamaConfigurationProfileAsync(id, Context.ConnectionAborted);

    public Task SetDefaultLlamaConfigurationProfile(Guid id) =>
        dataService.SetDefaultLlamaConfigurationProfileAsync(id, Context.ConnectionAborted);

    public ClientModelLoadStatus GetModelStatus() => ToClientStatus(modelLoader.GetStatus());

    public async Task<ClientModelLoadStatus> LoadModel(ClientLoadModelRequest request)
    {
        var advanced = request.Advanced;
        await modelLoader.LoadAsync(request.ModelPath, request.Backend, request.GpuLayerCount, request.ContextSize,
            request.VulkanDeviceWeights,
            new LlamaLoadOptions(advanced.MainGpu, advanced.SeqMax, advanced.RecurrentRollbackSnapshots, advanced.UseMemorymap,
                advanced.UseDirectIO, advanced.UseMemoryLock, advanced.Threads, advanced.BatchThreads, advanced.BatchSize,
                advanced.UBatchSize, advanced.Embeddings, advanced.NoKqvOffload, advanced.FlashAttention, advanced.VocabOnly,
                advanced.OpOffload, advanced.SwaFull, advanced.KVUnified, advanced.RopeFrequencyBase, advanced.RopeFrequencyScale,
                advanced.YarnExtrapolationFactor, advanced.YarnAttentionFactor, advanced.YarnBetaFast, advanced.YarnBetaSlow,
                advanced.YarnOriginalContext), Context.ConnectionAborted);
        return ToClientStatus(modelLoader.GetStatus());
    }

    public async Task<ClientModelLoadStatus> UnloadModel()
    {
        await modelLoader.StopAsync(Context.ConnectionAborted);
        return ToClientStatus(modelLoader.GetStatus());
    }

    public async Task<ClientModelLoadStatus> UnloadModelByPath(string modelPath)
    {
        await modelLoader.UnloadAsync(modelPath, Context.ConnectionAborted);
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