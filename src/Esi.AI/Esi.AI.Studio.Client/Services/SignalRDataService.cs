using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Components;
using Esi.AI.Models;
using Esi.AI.Studio.Contracts;
using System.Runtime.CompilerServices;

namespace Esi.AI.Studio.Client.Services;

public sealed class SignalRDataService : IDataService, IModelDownloadEvents, IModelRuntimeEvents, IBackendRequirementEvents, IBackendRuntimeEvents, IApplicationSettingsEvents, IProviderTraceEvents, IAsyncDisposable
{
    private readonly HubConnection connection;
    private readonly IClientStateStore stateStore;

    public event Func<ModelDownloadUpdate, Task>? ModelDownload_Create;
    public event Func<ModelDownloadUpdate, Task>? ModelDownload_Update;
    public event Func<ModelDownloadUpdate, Task>? ModelDownload_Delete;
    public event Func<IReadOnlyList<ModelDownloadUpdate>, Task>? ModelDownload_Read;
    public event Func<ModelLoadStatus, Task>? LoadedModel_Read;
    public event Func<ModelLoadStatus, Task>? LoadedModel_Create;
    public event Func<ModelLoadStatus, Task>? LoadedModel_Update;
    public event Func<ModelLoadStatus, Task>? LoadedModel_Delete;
    public event Func<BackendRequirementState, Task>? BackendRequirement_Read;
    public event Func<BackendRequirementState, Task>? BackendRequirement_Update;
    public event Func<IReadOnlyList<BackendRuntimeStatus>, Task>? BackendRuntime_Read;
    public event Func<BackendRuntimeStatus, Task>? BackendRuntime_Create;
    public event Func<BackendRuntimeStatus, Task>? BackendRuntime_Update;
    public event Func<BackendRuntimeStatus, Task>? BackendRuntime_Delete;
    public event Func<ApplicationSettings, Task>? ApplicationSettings_Update;
    public event Func<ProviderTraceEntry, Task>? ProviderTrace_Create;

    public SignalRDataService(NavigationManager navigationManager, IClientStateStore stateStore)
    {
        this.stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        connection = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/data"))
            .WithAutomaticReconnect()
            .WithServerTimeout(TimeSpan.FromMinutes(15))
            .WithKeepAliveInterval(TimeSpan.FromSeconds(10))
            .Build();
        connection.On<ModelDownloadUpdate>("ModelDownload_Create", async update =>
        {
            stateStore.ModelDownload_Create(update);
            var handler = ModelDownload_Create;
            if (handler is not null)
                await handler(update);
        });
        connection.On<ModelDownloadUpdate>("ModelDownload_Update", async update =>
        {
            stateStore.ModelDownload_Update(update);
            var handler = ModelDownload_Update;
            if (handler is not null)
                await handler(update);
        });
        connection.On<ModelDownloadUpdate>("ModelDownload_Delete", async update =>
        {
            stateStore.ModelDownload_Delete(update);
            var handler = ModelDownload_Delete;
            if (handler is not null)
                await handler(update);
        });
        connection.On<IReadOnlyList<ModelDownloadUpdate>>("ModelDownload_Read", async updates =>
        {
            stateStore.ModelDownload_Read(updates);
            var handler = ModelDownload_Read;
            if (handler is not null)
                await handler(updates);
        });
        connection.On<ModelLoadStatus>("LoadedModel_Read", async status =>
        {
            stateStore.LoadedModel_Read(status);
            var handler = LoadedModel_Read;
            if (handler is not null)
                await handler(status);
        });
            connection.On<ModelLoadStatus>("LoadedModel_Create", async status =>
            {
                stateStore.LoadedModel_Create(status);
                var handler = LoadedModel_Create;
                if (handler is not null)
                    await handler(status);
            });
            connection.On<ModelLoadStatus>("LoadedModel_Update", async status =>
            {
                stateStore.LoadedModel_Update(status);
                var handler = LoadedModel_Update;
                if (handler is not null)
                    await handler(status);
            });
            connection.On<ModelLoadStatus>("LoadedModel_Delete", async status =>
            {
                stateStore.LoadedModel_Delete(status);
                var handler = LoadedModel_Delete;
                if (handler is not null)
                    await handler(status);
            });
        connection.On<BackendRequirementState>("BackendRequirement_Read", async state =>
        {
            stateStore.BackendRequirement_Read(state);
            var handler = BackendRequirement_Read;
            if (handler is not null)
                await handler(state);
        });
        connection.On<BackendRequirementState>("BackendRequirement_Update", async state =>
        {
            stateStore.BackendRequirement_Update(state);
            var handler = BackendRequirement_Update;
            if (handler is not null)
                await handler(state);
        });
        connection.On<IReadOnlyList<BackendRuntimeStatus>>("BackendRuntime_Read", async statuses =>
        {
            stateStore.BackendRuntime_Read(statuses);
            var handler = BackendRuntime_Read;
            if (handler is not null)
                await handler(statuses);
        });
        connection.On<BackendRuntimeStatus>("BackendRuntime_Create", async status =>
        {
            stateStore.BackendRuntime_Create(status);
            var handler = BackendRuntime_Create;
            if (handler is not null)
                await handler(status);
        });
        connection.On<BackendRuntimeStatus>("BackendRuntime_Update", async status =>
        {
            stateStore.BackendRuntime_Update(status);
            var handler = BackendRuntime_Update;
            if (handler is not null)
                await handler(status);
        });
        connection.On<BackendRuntimeStatus>("BackendRuntime_Delete", async status =>
        {
            stateStore.BackendRuntime_Delete(status);
            var handler = BackendRuntime_Delete;
            if (handler is not null)
                await handler(status);
        });
            connection.On<ApplicationSettings>("ApplicationSettings_Update", async settings =>
            {
                var handler = ApplicationSettings_Update;
                if (handler is not null)
                await handler(settings);
            });
        connection.On<ProviderTraceEntry>("ProviderTrace_Create", async entry =>
        {
            var handler = ProviderTrace_Create;
            if (handler is not null)
                await handler(entry);
        });
    }

    public async Task<IReadOnlyList<ModelSettings>> ModelSettings_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<ModelSettings>>("ModelSettings_Read", cancellationToken);
    }

    public async Task<IReadOnlyList<FlowDefinition>> FlowDefinition_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<FlowDefinition>>("FlowDefinition_Read", cancellationToken);
    }

    public async Task<FlowDefinition> FlowDefinition_CreateAsync(FlowDefinition definition, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<FlowDefinition>("FlowDefinition_Create", definition, cancellationToken);
    }

    public async Task<FlowDefinition> FlowDefinition_UpdateAsync(FlowDefinition definition, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<FlowDefinition>("FlowDefinition_Update", definition, cancellationToken);
    }

    public async Task FlowDefinition_DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("FlowDefinition_Delete", id, cancellationToken);
    }

    public async Task ModelSettings_UpdateAsync(ModelSettings settings, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("ModelSettings_Update", settings, cancellationToken);
    }

    public async Task<ApplicationSettings> ApplicationSettings_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ApplicationSettings>("ApplicationSettings_Read", cancellationToken);
    }

    public async Task<ApplicationSettings> ApplicationSettings_UpdateAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("ApplicationSettings_Update", settings, cancellationToken);
        return settings;
    }

    public async Task<BackendRuntimeOptions> BackendRuntimePackage_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<BackendRuntimeOptions>("BackendRuntimePackage_Read", cancellationToken);
    }

    public async Task<BackendRuntimeOptions> BackendRuntimePackage_CreateAsync(BackendRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<BackendRuntimeOptions>("BackendRuntimePackage_Create", options, cancellationToken);
    }

    public async Task<BackendRuntimeOptions> BackendRuntimePackage_UpdateAsync(BackendRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<BackendRuntimeOptions>("BackendRuntimePackage_Update", options, cancellationToken);
    }

    public async Task BackendRuntimePackage_DeleteAsync(string packageId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("BackendRuntimePackage_Delete", packageId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderTraceEntry>> ProviderTrace_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<ProviderTraceEntry>>("ProviderTrace_Read", cancellationToken);
    }

    public async Task<IReadOnlyList<Model>> Model_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<Model>>("Model_Read", cancellationToken);
    }

    public async Task<IReadOnlyList<Model>> Model_UpdateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<Model>>("Model_Update", cancellationToken);
    }

    public async Task SetModelConfigurationAsync(string modelPath, Guid? configurationId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("Model_SetConfiguration", modelPath, configurationId, cancellationToken);
    }

    public async Task<IReadOnlyList<BackendModel>> BackendModel_ReadAsync(ConfigurationBackend backend, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<BackendModel>>("BackendModel_Read", backend, cancellationToken);
    }

    public async Task<IReadOnlyList<ModelConfiguration>> ModelConfiguration_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<ModelConfiguration>>("ModelConfiguration_Read", cancellationToken);
    }

    public async Task<ModelConfiguration?> ModelConfiguration_ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelConfiguration?>("ModelConfiguration_ReadById", id, cancellationToken);
    }

    public async Task<ModelConfiguration> ModelConfiguration_CreateAsync(ModelConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelConfiguration>("ModelConfiguration_Create", configuration, cancellationToken);
    }

    public async Task<ModelConfiguration> ModelConfiguration_UpdateAsync(ModelConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelConfiguration>("ModelConfiguration_Update", configuration, cancellationToken);
    }

    public async Task ModelConfiguration_DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("ModelConfiguration_Delete", id, cancellationToken);
    }

    public async Task ModelConfiguration_SetDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("ModelConfiguration_SetDefault", id, cancellationToken);
    }

    public async Task<ModelLoadStatus> LoadedModel_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        var status = await connection.InvokeAsync<ModelLoadStatus>("LoadedModel_Read", cancellationToken);
        stateStore.LoadedModel_Read(status);
        return status;
    }

    public async Task<ModelLoadStatus> LoadModelAsync(LoadModelRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelLoadStatus>("LoadModel", request, cancellationToken);
    }

    public async Task<ModelLoadStatus> LoadPythonModelAsync(PythonInferenceLoadRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelLoadStatus>("LoadPythonModel", request, cancellationToken);
    }

    public async Task<ModelLoadStatus> LoadDotLlmModelAsync(DotLlmLoadRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelLoadStatus>("LoadDotLlmModel", request, cancellationToken);
    }

    public async Task<ModelLoadStatus> UnloadModelAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelLoadStatus>("UnloadModel", cancellationToken);
    }

    public async Task<ModelLoadStatus> UnloadModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelLoadStatus>("UnloadModelByPath", modelPath, cancellationToken);
    }

    public async Task<ModelLoadStatus> UnloadModelAsync(string modelPath, ConfigurationBackend backend, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelLoadStatus>("UnloadModelByPathForBackend", modelPath, backend, cancellationToken);
    }

    public async Task<OpenVinoDiagnosticsDto> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<OpenVinoDiagnosticsDto>("GetOpenVinoDiagnostics", cancellationToken);
    }

    public async Task<OpenVinoSolveResultDto> SolveDiagnosticAsync(string checkId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<OpenVinoSolveResultDto>("SolveOpenVinoDiagnostic", checkId, cancellationToken);
    }

    public async Task<BackendPrerequisiteDiagnostics> GetBackendPrerequisitesAsync(ConfigurationBackend backend, string pythonExecutable = "python3", CancellationToken cancellationToken = default, IReadOnlyList<string>? devices = null)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<BackendPrerequisiteDiagnostics>("GetBackendPrerequisites", backend, pythonExecutable, devices, cancellationToken);
    }

    public async Task<BackendRequirementState> BackendRequirement_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        var state = await connection.InvokeAsync<BackendRequirementState>("BackendRequirement_Read", cancellationToken);
        stateStore.BackendRequirement_Read(state);
        return state;
    }

    public async Task<BackendRequirementState> BackendRequirement_UpdateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        var state = await connection.InvokeAsync<BackendRequirementState>("BackendRequirement_Update", cancellationToken);
        stateStore.BackendRequirement_Read(state);
        return state;
    }

    public async Task<BackendPrerequisiteSolveResult> PrepareBackendAsync(ConfigurationBackend backend, string pythonExecutable = "python3", CancellationToken cancellationToken = default, IReadOnlyList<string>? devices = null)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<BackendPrerequisiteSolveResult>("PrepareBackend", backend, pythonExecutable, devices, cancellationToken);
    }

    public async Task<IReadOnlyList<BackendRuntimeStatus>> BackendRuntime_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        var statuses = await connection.InvokeAsync<IReadOnlyList<BackendRuntimeStatus>>("BackendRuntime_Read", cancellationToken);
        stateStore.BackendRuntime_Read(statuses);
        return statuses;
    }

    public async Task<BackendRuntimeStatus> BackendRuntime_CreateAsync(BackendRuntimeInstallRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<BackendRuntimeStatus>("BackendRuntime_Create", request, cancellationToken);
    }

    public async Task<BackendRuntimeStatus> BackendRuntime_UpdateAsync(string packageId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<BackendRuntimeStatus>("BackendRuntime_Update", packageId, cancellationToken);
    }

    public async Task BackendRuntime_DeleteAsync(string packageId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("BackendRuntime_Delete", packageId, cancellationToken);
    }

    public async Task<OpenVinoLoadResultDto> LoadModelAsync(OpenVinoLoadRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<OpenVinoLoadResultDto>("LoadOpenVinoModel", request, cancellationToken);
    }

    public async Task CancelOpenVinoLoadAsync()
    {
        await EnsureConnectedAsync(CancellationToken.None);
        await connection.InvokeAsync("CancelOpenVinoLoad");
    }

    public async Task<OpenVinoModelStatusDto> GetOpenVinoModelStatusAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<OpenVinoModelStatusDto>("GetOpenVinoModelStatus", cancellationToken);
    }

    public async Task<IReadOnlyList<LocalModel>> LocalModel_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<LocalModel>>("LocalModel_Read", cancellationToken);
    }

    public async Task<IReadOnlyList<LocalModel>> LocalModel_UpdateAsync(ModelCompatibilityUpdate update, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<LocalModel>>("LocalModel_Update", update, cancellationToken);
    }

    public async Task<IReadOnlyList<LocalModel>> LocalModel_UpdateAsync(string modelPath, string huggingFaceModelId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<LocalModel>>("LocalModel_UpdateMetadata", modelPath, huggingFaceModelId, cancellationToken);
    }

    public async Task<IReadOnlyList<LocalModel>> LocalModel_DeleteAsync(ModelDeletionRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<LocalModel>>("LocalModel_Delete", request, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ModelDirectory_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<string>>("ModelDirectory_Read", cancellationToken);
    }

    public async Task<IReadOnlyList<HuggingFaceModel>> SearchModelsAsync(HuggingFaceSearchRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<HuggingFaceModel>>("SearchModels", request, cancellationToken);
    }

    public async Task<Guid> ModelDownload_CreateAsync(ModelDownloadRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<Guid>("ModelDownload_Create", request, cancellationToken);
    }

    public async Task<IReadOnlyList<ModelDownloadOption>> ModelDownload_ReadOptionsAsync(string modelId, string library = "gguf", CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<ModelDownloadOption>>("ModelDownload_ReadOptions", modelId, library, cancellationToken);
    }

    public async Task ModelDownload_UpdateAsync(Guid id, bool paused, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("ModelDownload_Update", id, paused, cancellationToken);
    }

    public async Task ModelDownload_DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("ModelDownload_Delete", id, cancellationToken);
    }

    public async Task ModelDownload_DeleteCompletedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("ModelDownload_DeleteCompleted", cancellationToken);
    }

    public async Task ModelDownload_DeleteFailedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("ModelDownload_DeleteFailed", cancellationToken);
    }

    public async Task<DownloadStatus?> ModelDownload_ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<DownloadStatus?>("ModelDownload_ReadById", id, cancellationToken);
    }

    public async Task<IReadOnlyList<DownloadStatus>> ModelDownload_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<DownloadStatus>>("ModelDownload_Read", cancellationToken);
    }

    public async Task<ModelStatus> SelectModelAsync(SelectModelRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelStatus>("SelectModel", request, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatSummary>> Chat_ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<ChatSummary>>("Chat_Read", cancellationToken);
    }

    public async Task<PersistedChat> Chat_CreateAsync(CreateChatRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<PersistedChat>("Chat_Create", request, cancellationToken);
    }

    public async Task<PersistedChat?> Chat_ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<PersistedChat?>("Chat_ReadById", id, cancellationToken);
    }

    public async Task Chat_DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("Chat_Delete", id, cancellationToken);
    }

    public async Task<PersistedChat?> Chat_UpdateAsync(Guid id, ChatExchangeRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<PersistedChat?>("Chat_Update", id, request, cancellationToken);
    }

    public async IAsyncEnumerable<ChatStreamUpdate> Chat_UpdateStreamAsync(Guid id, ChatExchangeRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await foreach (var update in connection.StreamAsync<ChatStreamUpdate>("Chat_UpdateStream", id, request, cancellationToken).ConfigureAwait(false))
            yield return update;
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        Exception? lastStartException = null;
        for (var attempt = 1; connection.State == HubConnectionState.Disconnected && attempt <= 10; attempt++)
        {
            try
            {
                await connection.StartAsync(cancellationToken);
                lastStartException = null;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastStartException = exception;
                if (attempt < 10)
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }

        while (connection.State is HubConnectionState.Connecting or HubConnectionState.Reconnecting)
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);

        if (connection.State != HubConnectionState.Connected)
        {
            if (lastStartException is not null)
                throw new InvalidOperationException("The data connection could not be established.", lastStartException);

            throw new InvalidOperationException("The data connection is not active.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
    }
}