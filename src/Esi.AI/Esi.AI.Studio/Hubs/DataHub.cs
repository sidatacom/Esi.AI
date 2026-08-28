using Esi.AI.Studio.Client.Services;
using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Microsoft.AspNetCore.SignalR;
using ClientLoadModelRequest = Esi.AI.Models.LoadModelRequest;
using ClientModelLoadStatus = Esi.AI.Studio.Client.Services.ModelLoadStatus;

namespace Esi.AI.Studio.Hubs;

public sealed class DataHub(
    DataService dataService,
    OpenVinoDiagnosticsService openVinoDiagnostics,
    OpenVinoDriverInstaller openVinoInstaller,
    ModelLibraryService modelLibrary) : Hub
{
    public Task<LlamaSettings?> GetLlamaSettings() => dataService.GetLlamaSettingsAsync(Context.ConnectionAborted);

    public Task SaveLlamaSettings(LlamaSettings settings) => dataService.SaveLlamaSettingsAsync(settings, Context.ConnectionAborted);

    public Task<OpenVinoSettings?> GetOpenVinoSettings() => dataService.GetOpenVinoSettingsAsync(Context.ConnectionAborted);

    public Task SaveOpenVinoSettings(OpenVinoSettings settings) => dataService.SaveOpenVinoSettingsAsync(settings, Context.ConnectionAborted);

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

    public Task<ClientModelLoadStatus> GetModelStatus() => dataService.GetModelStatusAsync(Context.ConnectionAborted);

    public Task<ClientModelLoadStatus> LoadModel(ClientLoadModelRequest request) =>
        dataService.LoadModelAsync(request, Context.ConnectionAborted);

    public Task<ClientModelLoadStatus> UnloadModel() =>
        dataService.UnloadModelAsync(Context.ConnectionAborted);

    public Task<ClientModelLoadStatus> UnloadModelByPath(string modelPath) =>
        dataService.UnloadModelAsync(modelPath, Context.ConnectionAborted);

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

    public Task<OpenVinoModelStatusDto> GetOpenVinoModelStatus() =>
        dataService.GetOpenVinoModelStatusAsync(Context.ConnectionAborted);

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
        return await dataService.AddChatExchangeAsync(id, request, Context.ConnectionAborted);
    }

}