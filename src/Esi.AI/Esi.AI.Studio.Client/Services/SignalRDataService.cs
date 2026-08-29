using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Components;
using Esi.AI.Models;

namespace Esi.AI.Studio.Client.Services;

public sealed class SignalRDataService : IDataService, IModelDownloadEvents, IModelRuntimeEvents, IAsyncDisposable
{
    private readonly HubConnection connection;

    public event Func<ModelDownloadUpdate, Task>? ModelDownloadUpdated;
    public event Func<Task>? ModelRuntimeStatusUpdated;

    public SignalRDataService(NavigationManager navigationManager)
    {
        connection = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/data"))
            .WithAutomaticReconnect()
            .WithServerTimeout(TimeSpan.FromMinutes(15))
            .WithKeepAliveInterval(TimeSpan.FromSeconds(10))
            .Build();
        connection.On<ModelDownloadUpdate>("ModelDownloadUpdated", async update =>
        {
            var handler = ModelDownloadUpdated;
            if (handler is not null)
                await handler(update);
        });
            connection.On("ModelRuntimeStatusUpdated", async () =>
            {
                var handler = ModelRuntimeStatusUpdated;
                if (handler is not null)
                await handler();
            });
    }

    public async Task<LlamaSettings?> GetLlamaSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<LlamaSettings?>("GetLlamaSettings", cancellationToken);
    }

    public async Task SaveLlamaSettingsAsync(LlamaSettings settings, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("SaveLlamaSettings", settings, cancellationToken);
    }

    public async Task<OpenVinoSettings?> GetOpenVinoSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<OpenVinoSettings?>("GetOpenVinoSettings", cancellationToken);
    }

    public async Task SaveOpenVinoSettingsAsync(OpenVinoSettings settings, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("SaveOpenVinoSettings", settings, cancellationToken);
    }

    public async Task<IReadOnlyList<LlamaModel>> GetLlamaModelsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<LlamaModel>>("GetLlamaModels", cancellationToken);
    }

    public async Task<IReadOnlyList<BackendModel>> GetBackendModelsAsync(ConfigurationBackend backend, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<BackendModel>>("GetBackendModels", backend, cancellationToken);
    }

    public async Task<IReadOnlyList<LlamaModel>> ScanLlamaModelsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<LlamaModel>>("ScanLlamaModels", cancellationToken);
    }

    public async Task SyncLlamaModelsAsync(IReadOnlyList<LlamaModel> models, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("SyncLlamaModels", models, cancellationToken);
    }

    public async Task SetModelConfigurationProfileAsync(string modelPath, Guid? profileId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("SetModelConfigurationProfile", modelPath, profileId, cancellationToken);
    }

    public async Task<IReadOnlyList<ModelConfigurationProfile>> GetModelConfigurationProfilesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<ModelConfigurationProfile>>("GetModelConfigurationProfiles", cancellationToken);
    }

    public async Task<ModelConfigurationProfile?> GetModelConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelConfigurationProfile?>("GetModelConfigurationProfile", id, cancellationToken);
    }

    public async Task<ModelConfigurationProfile> SaveModelConfigurationProfileAsync(ModelConfigurationProfile profile, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelConfigurationProfile>("SaveModelConfigurationProfile", profile, cancellationToken);
    }

    public async Task DeleteModelConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("DeleteModelConfigurationProfile", id, cancellationToken);
    }

    public async Task SetDefaultModelConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("SetDefaultModelConfigurationProfile", id, cancellationToken);
    }

    public async Task<ModelLoadStatus> GetModelStatusAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelLoadStatus>("GetModelStatus", cancellationToken);
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

    public async Task<BackendPrerequisiteDiagnostics> GetBackendPrerequisitesAsync(ConfigurationBackend backend, string pythonExecutable = "python3", CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<BackendPrerequisiteDiagnostics>("GetBackendPrerequisites", backend, pythonExecutable, cancellationToken);
    }

    public async Task<BackendPrerequisiteSolveResult> PrepareBackendAsync(ConfigurationBackend backend, string pythonExecutable = "python3", CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<BackendPrerequisiteSolveResult>("PrepareBackend", backend, pythonExecutable, cancellationToken);
    }

    public async Task<OpenVinoLoadResultDto> LoadModelAsync(OpenVinoLoadRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<OpenVinoLoadResultDto>("LoadOpenVinoModel", request, cancellationToken);
    }

    public async Task<OpenVinoModelStatusDto> GetOpenVinoModelStatusAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<OpenVinoModelStatusDto>("GetOpenVinoModelStatus", cancellationToken);
    }

    public async Task<IReadOnlyList<LocalModel>> ScanLocalModelsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<LocalModel>>("ScanLocalModels", cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetModelDirectoriesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<string>>("GetModelDirectories", cancellationToken);
    }

    public async Task<IReadOnlyList<HuggingFaceModel>> SearchModelsAsync(HuggingFaceSearchRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<HuggingFaceModel>>("SearchModels", request, cancellationToken);
    }

    public async Task<Guid> StartModelDownloadAsync(ModelDownloadRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<Guid>("StartModelDownload", request, cancellationToken);
    }

    public async Task<IReadOnlyList<ModelDownloadOption>> GetModelDownloadOptionsAsync(string modelId, string library = "gguf", CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<ModelDownloadOption>>("GetModelDownloadOptions", modelId, library, cancellationToken);
    }

    public async Task PauseModelDownloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("PauseModelDownload", id, cancellationToken);
    }

    public async Task ResumeModelDownloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await connection.InvokeAsync("ResumeModelDownload", id, cancellationToken);
    }

    public async Task<DownloadStatus?> GetModelDownloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<DownloadStatus?>("GetModelDownload", id, cancellationToken);
    }

    public async Task<ModelStatus> SelectModelAsync(SelectModelRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<ModelStatus>("SelectModel", request, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatSummary>> GetChatSummariesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<IReadOnlyList<ChatSummary>>("GetChatSummaries", cancellationToken);
    }

    public async Task<PersistedChat> CreateChatAsync(CreateChatRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<PersistedChat>("CreateChat", request, cancellationToken);
    }

    public async Task<PersistedChat?> GetChatAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<PersistedChat?>("GetChat", id, cancellationToken);
    }

    public async Task<PersistedChat?> AddChatExchangeAsync(Guid id, ChatExchangeRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        return await connection.InvokeAsync<PersistedChat?>("AddChatExchange", id, request, cancellationToken);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (connection.State == HubConnectionState.Disconnected)
            await connection.StartAsync(cancellationToken);

        while (connection.State is HubConnectionState.Connecting or HubConnectionState.Reconnecting)
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);

        if (connection.State != HubConnectionState.Connected)
            throw new InvalidOperationException("The data connection is not active.");
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
    }
}