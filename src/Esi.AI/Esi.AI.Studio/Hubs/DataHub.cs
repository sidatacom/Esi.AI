using Esi.AI.Studio.Client.Services;
using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Microsoft.AspNetCore.SignalR;

namespace Esi.AI.Studio.Hubs;

public sealed class DataHub(
    DataService dataService,
    OpenVinoDiagnosticsService openVinoDiagnostics,
    OpenVinoDriverInstaller openVinoInstaller,
    ModelLibraryService modelLibrary,
    BackendRequirementMonitor requirementMonitor) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        foreach (var download in dataService.ModelDownload_Read())
            await Clients.Caller.SendAsync("ModelDownload_Create", new ModelDownloadUpdate(download), Context.ConnectionAborted);
        await Clients.Caller.SendAsync("LoadedModel_Update", await dataService.LoadedModel_ReadAsync(Context.ConnectionAborted), Context.ConnectionAborted);
    }

    public Task<IReadOnlyList<ModelSettings>> ModelSettings_Read() => dataService.ModelSettings_ReadAsync(Context.ConnectionAborted);

    public Task ModelSettings_Update(ModelSettings settings) => dataService.ModelSettings_UpdateAsync(settings, Context.ConnectionAborted);

    public Task<IReadOnlyList<Model>> Model_Read() => dataService.Model_ReadAsync(Context.ConnectionAborted);

    public Task<IReadOnlyList<Model>> Model_Update() => dataService.Model_UpdateAsync(Context.ConnectionAborted);

    public Task Model_SetConfiguration(string modelPath, Guid? configurationId) =>
        dataService.SetModelConfigurationAsync(modelPath, configurationId, Context.ConnectionAborted);

    public Task<IReadOnlyList<BackendModel>> BackendModel_Read(ConfigurationBackend backend) =>
        dataService.BackendModel_ReadAsync(backend, Context.ConnectionAborted);

    public Task<IReadOnlyList<ModelConfiguration>> ModelConfiguration_Read() =>
        dataService.ModelConfiguration_ReadAsync(Context.ConnectionAborted);

    public Task<ModelConfiguration?> ModelConfiguration_ReadById(Guid id) =>
        dataService.ModelConfiguration_ReadAsync(id, Context.ConnectionAborted);

    public Task<ModelConfiguration> ModelConfiguration_Create(ModelConfiguration configuration) =>
        dataService.ModelConfiguration_CreateAsync(configuration, Context.ConnectionAborted);

    public Task<ModelConfiguration> ModelConfiguration_Update(ModelConfiguration configuration) =>
        dataService.ModelConfiguration_UpdateAsync(configuration, Context.ConnectionAborted);

    public Task ModelConfiguration_Delete(Guid id) =>
        dataService.ModelConfiguration_DeleteAsync(id, Context.ConnectionAborted);

    public Task ModelConfiguration_SetDefault(Guid id) =>
        dataService.ModelConfiguration_SetDefaultAsync(id, Context.ConnectionAborted);

    public Task<ModelLoadStatus> LoadedModel_Read() => dataService.LoadedModel_ReadAsync(Context.ConnectionAborted);

    public Task<ModelLoadStatus> LoadModel(LoadModelRequest request) =>
        dataService.LoadModelAsync(request, CancellationToken.None);

    public Task<ModelLoadStatus> LoadPythonModel(PythonInferenceLoadRequest request) =>
        dataService.LoadPythonModelAsync(request, CancellationToken.None);

    public Task<ModelLoadStatus> LoadDotLlmModel(DotLlmLoadRequest request) =>
        dataService.LoadDotLlmModelAsync(request, CancellationToken.None);

    public Task<ModelLoadStatus> UnloadModel() =>
        dataService.UnloadModelAsync(Context.ConnectionAborted);

    public Task<ModelLoadStatus> UnloadModelByPath(string modelPath) =>
        dataService.UnloadModelAsync(modelPath, Context.ConnectionAborted);

    public Task<ModelLoadStatus> UnloadModelByPathForBackend(string modelPath, ConfigurationBackend backend) =>
        dataService.UnloadModelAsync(modelPath, backend, Context.ConnectionAborted);

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

    public Task<BackendPrerequisiteDiagnostics> GetBackendPrerequisites(ConfigurationBackend backend, string pythonExecutable, IReadOnlyList<string>? devices) =>
        dataService.GetBackendPrerequisitesAsync(backend, pythonExecutable, Context.ConnectionAborted, devices);

    public Task<BackendRequirementState> GetBackendRequirementState() =>
        Task.FromResult(requirementMonitor.Current);

    public async Task<BackendPrerequisiteSolveResult> PrepareBackend(ConfigurationBackend backend, string pythonExecutable, IReadOnlyList<string>? devices)
    {
        if (backend is not (ConfigurationBackend.Vllm or ConfigurationBackend.Sglang))
            return new(false, "Only Python backends can be prepared from this tile.", string.Empty);

        var result = await dataService.PrepareBackendAsync(backend, pythonExecutable, Context.ConnectionAborted, devices);
        requirementMonitor.RequestRefresh();
        return result;
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
        dataService.LoadModelAsync(request, CancellationToken.None);

    public Task<OpenVinoModelStatusDto> GetOpenVinoModelStatus() =>
        dataService.GetOpenVinoModelStatusAsync(Context.ConnectionAborted);

    public Task<IReadOnlyList<LocalModel>> LocalModel_Read() =>
        dataService.LocalModel_ReadAsync(Context.ConnectionAborted);

    public Task<IReadOnlyList<LocalModel>> LocalModel_Update(ModelCompatibilityUpdate update) =>
        dataService.LocalModel_UpdateAsync(update, Context.ConnectionAborted);

    public Task<IReadOnlyList<LocalModel>> LocalModel_UpdateMetadata(string modelPath, string huggingFaceModelId) =>
        dataService.LocalModel_UpdateAsync(modelPath, huggingFaceModelId, Context.ConnectionAborted);

    public Task<IReadOnlyList<LocalModel>> LocalModel_Delete(ModelDeletionRequest request) =>
        dataService.LocalModel_DeleteAsync(request, Context.ConnectionAborted);

    public IReadOnlyList<string> ModelDirectory_Read() => modelLibrary.GetModelDirectories();

    public async Task<IReadOnlyList<HuggingFaceModel>> SearchModels(HuggingFaceSearchRequest request) =>
        await dataService.SearchModelsAsync(request, Context.ConnectionAborted);

    public Task<Guid> ModelDownload_Create(ModelDownloadRequest request) =>
        dataService.ModelDownload_CreateAsync(request, Context.ConnectionAborted);

    public Task<IReadOnlyList<ModelDownloadOption>> ModelDownload_ReadOptions(string modelId, string library = "gguf") =>
        dataService.ModelDownload_ReadOptionsAsync(modelId, library, Context.ConnectionAborted);

    public Task ModelDownload_Update(Guid id, bool paused) =>
        dataService.ModelDownload_UpdateAsync(id, paused, Context.ConnectionAborted);

    public Task ModelDownload_Delete(Guid id) =>
        dataService.ModelDownload_DeleteAsync(id, Context.ConnectionAborted);

    public Task<DownloadStatus?> ModelDownload_ReadById(Guid id) =>
        Task.FromResult(dataService.ModelDownload_Read(id));

    public Task<IReadOnlyList<DownloadStatus>> ModelDownload_Read() =>
        Task.FromResult(dataService.ModelDownload_Read());

    public Task<ModelStatus> SelectModel(SelectModelRequest request) =>
        dataService.SelectModelAsync(request, Context.ConnectionAborted);

    public Task<IReadOnlyList<ChatSummary>> Chat_Read() =>
        dataService.Chat_ReadAsync(Context.ConnectionAborted);

    public Task<PersistedChat> Chat_Create(CreateChatRequest request) =>
        dataService.Chat_CreateAsync(request, Context.ConnectionAborted);

    public Task<PersistedChat?> Chat_ReadById(Guid id) =>
        dataService.Chat_ReadAsync(id, Context.ConnectionAborted);

    public Task Chat_Delete(Guid id) =>
        dataService.Chat_DeleteAsync(id, Context.ConnectionAborted);

    public Task<PersistedChat?> Chat_Update(Guid id, ChatExchangeRequest request) =>
        dataService.Chat_UpdateAsync(id, request, Context.ConnectionAborted);

    public IAsyncEnumerable<ChatStreamUpdate> Chat_UpdateStream(Guid id, ChatExchangeRequest request) =>
        dataService.Chat_UpdateStreamAsync(id, request, Context.ConnectionAborted);

}