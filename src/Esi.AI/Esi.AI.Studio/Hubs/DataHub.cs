using Esi.AI.Studio.Client.Services;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Core.Chat;
using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Microsoft.AspNetCore.SignalR;
using ClientLoadModelRequest = Esi.AI.Models.LoadModelRequest;
using ClientModelLoadStatus = Esi.AI.Studio.Client.Services.ModelLoadStatus;

namespace Esi.AI.Studio.Hubs;

public sealed class DataHub(
    DataService dataService,
    LlamaModelLoader modelLoader,
    OpenVinoDiagnosticsService openVinoDiagnostics,
    OpenVinoDriverInstaller openVinoInstaller,
    ModelLibraryService modelLibrary) : Hub
{
    public Task<LlamaSettings?> GetLlamaSettings() => dataService.GetLlamaSettingsAsync(Context.ConnectionAborted);

    public Task SaveLlamaSettings(LlamaSettings settings) => dataService.SaveLlamaSettingsAsync(settings, Context.ConnectionAborted);

    public Task<IReadOnlyList<LlamaModel>> GetLlamaModels() => dataService.GetLlamaModelsAsync(Context.ConnectionAborted);

    public Task<IReadOnlyList<LlamaModel>> ScanLlamaModels() => dataService.ScanLlamaModelsAsync(Context.ConnectionAborted);

    public Task SyncLlamaModels(IReadOnlyList<LlamaModel> models) => dataService.SyncLlamaModelsAsync(models, Context.ConnectionAborted);

    public Task SetModelConfigurationProfile(string modelPath, Guid? profileId) =>
        dataService.SetModelConfigurationProfileAsync(modelPath, profileId, Context.ConnectionAborted);

    public Task<IReadOnlyList<ModelConfigurationProfile>> GetModelConfigurationProfiles() =>
        dataService.GetModelConfigurationProfilesAsync(Context.ConnectionAborted);

    public Task<ModelConfigurationProfile?> GetModelConfigurationProfile(Guid id) =>
        dataService.GetModelConfigurationProfileAsync(id, Context.ConnectionAborted);

    public Task<ModelConfigurationProfile> SaveModelConfigurationProfile(ModelConfigurationProfile profile) =>
        dataService.SaveModelConfigurationProfileAsync(profile, Context.ConnectionAborted);

    public Task DeleteModelConfigurationProfile(Guid id) =>
        dataService.DeleteModelConfigurationProfileAsync(id, Context.ConnectionAborted);

    public Task SetDefaultModelConfigurationProfile(Guid id) =>
        dataService.SetDefaultModelConfigurationProfileAsync(id, Context.ConnectionAborted);

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

    public OpenVinoDiagnosticsDto GetOpenVinoDiagnostics()
    {
        var result = openVinoDiagnostics.Diagnose();
        return new OpenVinoDiagnosticsDto
        {
            IsGpuReady = result.IsGpuReady,
            IsNpuReady = result.IsNpuReady,
            Devices = result.Devices.Select(device => new OpenVinoDeviceDto
            {
                Id = device.Id,
                Name = device.Name,
                IsCompatible = device.IsCompatible,
                Detail = device.Detail
            }).ToArray(),
            Checks = result.Checks.Select(check => new OpenVinoDiagnosticCheckDto
            {
                Name = check.Name,
                Id = check.Id,
                IsAvailable = check.IsAvailable,
                Detail = check.Detail,
                CanSolve = check.CanSolve
            }).ToArray(),
            Error = result.Error
        };
    }

    public async Task<OpenVinoSolveResultDto> SolveOpenVinoDiagnostic(string checkId)
    {
        var remoteAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress;
        if (remoteAddress is null || !System.Net.IPAddress.IsLoopback(remoteAddress))
        {
            return new OpenVinoSolveResultDto
            {
                Succeeded = false,
                Message = "Driver installation is only available from the local machine.",
                Output = $"Remote address rejected: {remoteAddress?.ToString() ?? "unknown"}"
            };
        }

        var result = checkId switch
        {
            "level-zero-loader" or "intel-level-zero-gpu" => await openVinoInstaller.InstallAsync(Context.ConnectionAborted),
            "render-permissions" => await openVinoInstaller.AddUserToRenderGroupsAsync(Context.ConnectionAborted),
            _ => new OpenVinoInstallResult(false, "This diagnostic cannot be repaired automatically.", string.Empty)
        };
        return new OpenVinoSolveResultDto
        {
            Succeeded = result.Succeeded,
            Message = result.Message,
            Output = string.IsNullOrWhiteSpace(result.Output)
                ? "No installer output was returned by the server."
                : result.Output
        };
    }

    public Task<OpenVinoLoadResultDto> LoadOpenVinoModel(OpenVinoLoadRequest request) =>
        dataService.LoadModelAsync(request, Context.ConnectionAborted);

    public async Task<IReadOnlyList<LocalModel>> ScanLocalModels() =>
        await dataService.ScanLocalModelsAsync(Context.ConnectionAborted);

    public IReadOnlyList<string> GetModelDirectories() => modelLibrary.GetModelDirectories();

    public async Task<IReadOnlyList<HuggingFaceModel>> SearchModels(string query) =>
        await dataService.SearchModelsAsync(query, Context.ConnectionAborted);

    public Task<Guid> StartModelDownload(ModelDownloadRequest request) =>
        dataService.StartModelDownloadAsync(request, Context.ConnectionAborted);

    public Task<DownloadStatus?> GetModelDownload(Guid id) =>
        Task.FromResult(dataService.GetModelDownload(id));

    public Task<ModelStatus> SelectModel(SelectModelRequest request) =>
        dataService.SelectModelAsync(request, Context.ConnectionAborted);

    public Task<IReadOnlyList<ChatSummary>> GetChatSummaries() =>
        dataService.GetChatSummariesAsync(Context.ConnectionAborted);

    public Task<PersistedChat> CreateChat(CreateChatRequest request) =>
        dataService.CreateChatAsync(request.Title, Context.ConnectionAborted);

    public Task<PersistedChat?> GetChat(Guid id) =>
        dataService.GetChatAsync(id, Context.ConnectionAborted);

    public async Task<PersistedChat?> AddChatExchange(Guid id, ChatExchangeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.ModelPath))
            return null;
        var chat = await dataService.GetChatAsync(id, Context.ConnectionAborted);
        if (chat is null)
            return null;
        using var session = modelLoader.CreateChatSession("You are a helpful assistant.", request.ModelPath);
        var messages = chat.Messages.Select(message => new LlamaChatMessage(message.Role, message.Content))
            .Append(new LlamaChatMessage("user", request.Content.Trim())).ToArray();
        var generation = await session.GenerateWithStatsAsync(messages, Context.ConnectionAborted);
        return await dataService.AddChatExchangeAsync(id, request.Content.Trim(), generation, request.ModelPath, Context.ConnectionAborted);
    }

    private static ClientModelLoadStatus ToClientStatus(Esi.AI.Core.ModelLoading.ModelLoadStatus status) =>
        new(status.ModelPath, status.Backend, status.GpuLayerCount, status.ContextSize, status.ModelSizeInBytes,
            status.FoundVulkanGpuCount,
            status.VulkanDevices.Select(device => new Esi.AI.Studio.Client.Services.VulkanDeviceStatus(device.Name, device.Description, device.AssignedLayerCount, device.ModelBufferMiB)).ToArray(),
            status.CpuModelBufferMiB, status.LoadLog, status.VulkanDeviceWeights, status.IsModelLoaded,
            status.LoadedModels.Select(ToClientLoadedModelStatus).ToArray());

    private static Esi.AI.Studio.Client.Services.LoadedModelStatus ToClientLoadedModelStatus(Esi.AI.Core.ModelLoading.LoadedModelStatus model) =>
        new(model.ModelPath, model.Backend, model.GpuLayerCount, model.ContextSize, model.ModelSizeInBytes,
            model.VulkanDevices.Select(device => new Esi.AI.Studio.Client.Services.VulkanDeviceStatus(device.Name, device.Description, device.AssignedLayerCount, device.ModelBufferMiB)).ToArray(),
            model.CpuModelBufferMiB);
}