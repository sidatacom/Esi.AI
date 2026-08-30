using Esi.AI.Models;

namespace Esi.AI.Studio.Client.Services;

public interface IModelDownloadEvents
{
    event Func<ModelDownloadUpdate, Task>? ModelDownload_Create;
    event Func<ModelDownloadUpdate, Task>? ModelDownload_Update;
    event Func<ModelDownloadUpdate, Task>? ModelDownload_Delete;
}

public interface IModelRuntimeEvents
{
    event Func<ModelLoadStatus, Task>? LoadedModel_Create;
    event Func<ModelLoadStatus, Task>? LoadedModel_Update;
    event Func<ModelLoadStatus, Task>? LoadedModel_Delete;
}

public interface IBackendRequirementEvents
{
    event Func<BackendRequirementState, Task>? BackendRequirementStateUpdated;
}

public interface IDataService
{
    Task<PersistedChat> Chat_CreateAsync(CreateChatRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatSummary>> Chat_ReadAsync(CancellationToken cancellationToken = default);
    Task<PersistedChat?> Chat_ReadAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PersistedChat?> Chat_UpdateAsync(Guid id, ChatExchangeRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ChatStreamUpdate> Chat_UpdateStreamAsync(Guid id, ChatExchangeRequest request, CancellationToken cancellationToken = default);
    Task Chat_DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelSettings>> ModelSettings_ReadAsync(CancellationToken cancellationToken = default);

    Task ModelSettings_UpdateAsync(ModelSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Model>> Model_ReadAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackendModel>> BackendModel_ReadAsync(ConfigurationBackend backend, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Model>> Model_UpdateAsync(CancellationToken cancellationToken = default);

    Task SetModelConfigurationAsync(string modelPath, Guid? configurationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelConfiguration>> ModelConfiguration_ReadAsync(CancellationToken cancellationToken = default);

    Task<ModelConfiguration?> ModelConfiguration_ReadAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ModelConfiguration> ModelConfiguration_CreateAsync(ModelConfiguration configuration, CancellationToken cancellationToken = default);

    Task<ModelConfiguration> ModelConfiguration_UpdateAsync(ModelConfiguration configuration, CancellationToken cancellationToken = default);

    Task ModelConfiguration_DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task ModelConfiguration_SetDefaultAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalModel>> LocalModel_ReadAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalModel>> LocalModel_UpdateAsync(ModelCompatibilityUpdate update, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalModel>> LocalModel_UpdateAsync(string modelPath, string huggingFaceModelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalModel>> LocalModel_DeleteAsync(ModelDeletionRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ModelDirectory_ReadAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HuggingFaceModel>> SearchModelsAsync(HuggingFaceSearchRequest request, CancellationToken cancellationToken = default);
    Task<Guid> ModelDownload_CreateAsync(ModelDownloadRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModelDownloadOption>> ModelDownload_ReadOptionsAsync(string modelId, string library = "gguf", CancellationToken cancellationToken = default);
    Task<DownloadStatus?> ModelDownload_ReadAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadStatus>> ModelDownload_ReadAsync(CancellationToken cancellationToken = default);
    Task ModelDownload_UpdateAsync(Guid id, bool paused, CancellationToken cancellationToken = default);
    Task ModelDownload_DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ModelStatus> SelectModelAsync(SelectModelRequest request, CancellationToken cancellationToken = default);
    Task<ModelLoadStatus> LoadedModel_ReadAsync(CancellationToken cancellationToken = default);

    Task<ModelLoadStatus> LoadModelAsync(LoadModelRequest request, CancellationToken cancellationToken = default);
    Task<ModelLoadStatus> LoadPythonModelAsync(PythonInferenceLoadRequest request, CancellationToken cancellationToken = default);
    Task<ModelLoadStatus> LoadDotLlmModelAsync(DotLlmLoadRequest request, CancellationToken cancellationToken = default);

    Task<ModelLoadStatus> UnloadModelAsync(CancellationToken cancellationToken = default);

    Task<ModelLoadStatus> UnloadModelAsync(string modelPath, CancellationToken cancellationToken = default);

    Task<ModelLoadStatus> UnloadModelAsync(string modelPath, ConfigurationBackend backend, CancellationToken cancellationToken = default);

    Task<OpenVinoDiagnosticsDto> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
    Task<OpenVinoSolveResultDto> SolveDiagnosticAsync(string checkId, CancellationToken cancellationToken = default);
    Task<BackendPrerequisiteDiagnostics> GetBackendPrerequisitesAsync(ConfigurationBackend backend, string pythonExecutable = "python3", CancellationToken cancellationToken = default, IReadOnlyList<string>? devices = null);
    Task<BackendRequirementState> GetBackendRequirementStateAsync(CancellationToken cancellationToken = default);
    Task<BackendPrerequisiteSolveResult> PrepareBackendAsync(ConfigurationBackend backend, string pythonExecutable = "python3", CancellationToken cancellationToken = default, IReadOnlyList<string>? devices = null);
    Task<OpenVinoLoadResultDto> LoadModelAsync(OpenVinoLoadRequest request, CancellationToken cancellationToken = default);
    Task<OpenVinoModelStatusDto> GetOpenVinoModelStatusAsync(CancellationToken cancellationToken = default);
}

