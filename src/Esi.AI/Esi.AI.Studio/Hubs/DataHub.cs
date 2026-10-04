using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Contracts;
using Esi.AI.Studio.Services;
using Microsoft.AspNetCore.SignalR;
using System.Net;

namespace Esi.AI.Studio.Hubs;

public sealed class DataHub(
    DataService dataService,
    IBackendDiagnosticsService backendDiagnostics,
    IModelDirectoryCatalog modelDirectories,
    IBackendRequirementState requirementMonitor) : Hub
{
    public const string LocalTrainingClientsGroup = "local-training-clients";

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        if (IsLocalTrainingClient())
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LocalTrainingClientsGroup, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("TrainingRun_Read", await dataService.TrainingRun_ReadAsync(Context.ConnectionAborted), Context.ConnectionAborted);
        }

        await Clients.Caller.SendAsync("ModelDownload_Read", dataService.ModelDownload_Read().Select(download => new ModelDownloadUpdate(download)).ToArray(), Context.ConnectionAborted);
        await Clients.Caller.SendAsync("VulkanLog_Read", await dataService.VulkanLog_ReadAsync(Context.ConnectionAborted), Context.ConnectionAborted);
        await Clients.Caller.SendAsync("LoadedModel_Read", await dataService.LoadedModel_ReadAsync(Context.ConnectionAborted), Context.ConnectionAborted);
        try
        {
            await Clients.Caller.SendAsync("BackendRuntime_Read", await dataService.BackendRuntime_ReadAsync(Context.ConnectionAborted), Context.ConnectionAborted);
        }
        catch (OperationCanceledException)
        {
            // A reconnect can cancel the optional runtime catalog refresh; keep the SignalR connection usable.
        }
    }

    public Task<IReadOnlyList<ModelSettings>> ModelSettings_Read() => dataService.ModelSettings_ReadAsync(Context.ConnectionAborted);

    public Task<IReadOnlyList<FlowDefinition>> FlowDefinition_Read() =>
        dataService.FlowDefinition_ReadAsync(Context.ConnectionAborted);

    public Task<FlowDefinition> FlowDefinition_Create(FlowDefinition definition) =>
        dataService.FlowDefinition_CreateAsync(definition, Context.ConnectionAborted);

    public Task<FlowDefinition> FlowDefinition_Update(FlowDefinition definition) =>
        dataService.FlowDefinition_UpdateAsync(definition, Context.ConnectionAborted);

    public Task FlowDefinition_Delete(Guid id) =>
        dataService.FlowDefinition_DeleteAsync(id, Context.ConnectionAborted);

    public Task<ApplicationSettings> ApplicationSettings_Read() => dataService.ApplicationSettings_ReadAsync(Context.ConnectionAborted);

    public Task<IReadOnlyList<ProviderTraceEntry>> ProviderTrace_Read() =>
        dataService.ProviderTrace_ReadAsync(Context.ConnectionAborted);

    public async Task ApplicationSettings_Update(ApplicationSettings settings)
    {
        var updated = await dataService.ApplicationSettings_UpdateAsync(settings, Context.ConnectionAborted);
        await Clients.All.SendAsync("ApplicationSettings_Update", updated, Context.ConnectionAborted);
    }

    public Task<BackendRuntimeOptions> BackendRuntimePackage_Read() =>
        dataService.BackendRuntimePackage_ReadAsync(Context.ConnectionAborted);

    public Task<BackendRuntimeOptions> BackendRuntimePackage_Create(BackendRuntimeOptions options) =>
        dataService.BackendRuntimePackage_CreateAsync(options, Context.ConnectionAborted);

    public Task<BackendRuntimeOptions> BackendRuntimePackage_Update(BackendRuntimeOptions options) =>
        dataService.BackendRuntimePackage_UpdateAsync(options, Context.ConnectionAborted);

    public Task BackendRuntimePackage_Delete(string packageId) =>
        dataService.BackendRuntimePackage_DeleteAsync(packageId, Context.ConnectionAborted);

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

    public Task<ModelLoadStatus> UnloadModel() =>
        dataService.UnloadModelAsync(Context.ConnectionAborted);

    public Task<ModelLoadStatus> UnloadModelByPath(string modelPath) =>
        dataService.UnloadModelAsync(modelPath, Context.ConnectionAborted);

    public Task<ModelLoadStatus> UnloadModelByPathForBackend(string modelPath, ConfigurationBackend backend, string backendVariantId = "") =>
        dataService.UnloadModelAsync(modelPath, backend, Context.ConnectionAborted, backendVariantId);

    public Task<OpenVinoDiagnosticsDto> GetOpenVinoDiagnostics() =>
        backendDiagnostics.GetOpenVinoDiagnosticsAsync(Context.ConnectionAborted);

    public Task<BackendPrerequisiteDiagnostics> GetBackendPrerequisites(ConfigurationBackend backend, string pythonExecutable, IReadOnlyList<string>? devices) =>
        dataService.GetBackendPrerequisitesAsync(backend, pythonExecutable, Context.ConnectionAborted, devices);

    public Task<IReadOnlyList<BackendAcceleratorDevice>> BackendDevice_Read(string backendVariantId) =>
        dataService.BackendDevice_ReadAsync(backendVariantId, Context.ConnectionAborted);

    public Task<VulkanLogStatus> VulkanLog_Create() =>
        dataService.VulkanLog_CreateAsync(Context.ConnectionAborted);

    public Task<IReadOnlyList<VulkanLogStatus>> VulkanLog_Read() =>
        dataService.VulkanLog_ReadAsync(Context.ConnectionAborted);

    public Task<VulkanLogStatus> VulkanLog_Update(Guid id) =>
        dataService.VulkanLog_UpdateAsync(id, Context.ConnectionAborted);

    public Task VulkanLog_Delete(Guid id) =>
        dataService.VulkanLog_DeleteAsync(id, Context.ConnectionAborted);

    public Task<BackendRequirementState> BackendRequirement_Read() =>
        Task.FromResult(requirementMonitor.Current);

    public Task<BackendRequirementState> BackendRequirement_Update() =>
        dataService.BackendRequirement_UpdateAsync(Context.ConnectionAborted);

    public Task<IReadOnlyList<BackendRuntimeStatus>> BackendRuntime_Read() =>
        dataService.BackendRuntime_ReadAsync(Context.ConnectionAborted);

    public Task<BackendRuntimeStatus> BackendRuntime_Create(BackendRuntimeInstallRequest request)
    {
        var remoteAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress;
        if (remoteAddress is null || !System.Net.IPAddress.IsLoopback(remoteAddress))
        {
            return Task.FromResult(new BackendRuntimeStatus(
                request.PackageId,
                ConfigurationBackend.Llama,
                string.Empty,
                string.Empty,
                string.Empty,
                BackendRuntimeState.Failed,
                "Backend runtime installation is only available from the local machine.",
                false,
                true,
                DateTimeOffset.UtcNow));
        }

        return dataService.BackendRuntime_CreateAsync(request, Context.ConnectionAborted);
    }

    public Task<BackendRuntimeStatus> BackendRuntime_Update(string packageId) =>
        dataService.BackendRuntime_UpdateAsync(packageId, Context.ConnectionAborted);

    public Task BackendRuntime_Delete(string packageId) =>
        dataService.BackendRuntime_DeleteAsync(packageId, Context.ConnectionAborted);

    public async Task<BackendPrerequisiteSolveResult> PrepareBackend(ConfigurationBackend backend, string pythonExecutable, IReadOnlyList<string>? devices)
    {
        if (backend is not (ConfigurationBackend.Llama or ConfigurationBackend.Vllm))
            return new(false, "This backend has no preparation action from this tile.", string.Empty);

        var result = await dataService.PrepareBackendAsync(backend, pythonExecutable, Context.ConnectionAborted, devices);
        requirementMonitor.RequestRefresh(backend, devices);
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

        return await backendDiagnostics.SolveOpenVinoDiagnosticAsync(checkId, Context.ConnectionAborted);
    }

    public Task<OpenVinoLoadResultDto> LoadOpenVinoModel(OpenVinoLoadRequest request) =>
        dataService.LoadModelAsync(request, CancellationToken.None);

    public Task CancelOpenVinoLoad() =>
        dataService.CancelOpenVinoLoadAsync();

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

    public Task<ModelCleanupPreview> LocalModel_CleanPreview(ModelCleanupRequest request) =>
        dataService.LocalModel_CleanPreviewAsync(request, Context.ConnectionAborted);

    public Task<ModelCleanupResult> LocalModel_Clean(ModelCleanupRequest request) =>
        dataService.LocalModel_CleanAsync(request, Context.ConnectionAborted);

    public IReadOnlyList<string> ModelDirectory_Read() => modelDirectories.GetModelDirectories();

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

    public Task ModelDownload_DeleteCompleted() =>
        dataService.ModelDownload_DeleteCompletedAsync(Context.ConnectionAborted);

    public Task ModelDownload_DeleteFailed() =>
        dataService.ModelDownload_DeleteFailedAsync(Context.ConnectionAborted);

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

    public Task<TrainingRunStatus> TrainingRun_Create(CreateTrainingRunRequest request)
    {
        EnsureLocalTrainingClient();
        return dataService.TrainingRun_CreateAsync(request, Context.ConnectionAborted);
    }

    public Task<IReadOnlyList<TrainingRunStatus>> TrainingRun_Read()
    {
        EnsureLocalTrainingClient();
        return dataService.TrainingRun_ReadAsync(Context.ConnectionAborted);
    }

    public Task<TrainingRunStatus?> TrainingRun_Update(Guid id)
    {
        EnsureLocalTrainingClient();
        return dataService.TrainingRun_UpdateAsync(id, Context.ConnectionAborted);
    }

    public Task TrainingRun_Delete(Guid id)
    {
        EnsureLocalTrainingClient();
        return dataService.TrainingRun_DeleteAsync(id, Context.ConnectionAborted);
    }

    public Task<string> TrainingRun_SampleDataset_Create()
    {
        EnsureLocalTrainingClient();
        return dataService.TrainingRun_SampleDataset_CreateAsync(Context.ConnectionAborted);
    }

    private void EnsureLocalTrainingClient()
    {
        if (!IsLocalTrainingClient())
            throw new HubException("PyTorch training is available only to local Studio clients.");
    }

    private bool IsLocalTrainingClient()
    {
        var remoteAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress;
        return remoteAddress is not null &&
            (IPAddress.IsLoopback(remoteAddress) ||
             remoteAddress.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remoteAddress.MapToIPv4()));
    }

}