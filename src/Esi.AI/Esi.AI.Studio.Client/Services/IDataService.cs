using Esi.AI.Models;

namespace Esi.AI.Studio.Client.Services;

public interface IModelDownloadEvents
{
    event Func<ModelDownloadUpdate, Task>? ModelDownloadUpdated;
}

public interface IModelRuntimeEvents
{
    event Func<Task>? ModelRuntimeStatusUpdated;
}

public interface IDataService
{
    Task<PersistedChat> CreateChatAsync(CreateChatRequest request, CancellationToken cancellationToken = default);
    Task<PersistedChat?> GetChatAsync(Guid id, CancellationToken cancellationToken = default);

    Task<LlamaSettings?> GetLlamaSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveLlamaSettingsAsync(LlamaSettings settings, CancellationToken cancellationToken = default);

    Task<OpenVinoSettings?> GetOpenVinoSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveOpenVinoSettingsAsync(OpenVinoSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LlamaModel>> GetLlamaModelsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LlamaModel>> ScanLlamaModelsAsync(CancellationToken cancellationToken = default);

    Task SyncLlamaModelsAsync(IReadOnlyList<LlamaModel> models, CancellationToken cancellationToken = default);

    Task SetModelConfigurationProfileAsync(string modelPath, Guid? profileId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelConfigurationProfile>> GetModelConfigurationProfilesAsync(CancellationToken cancellationToken = default);

    Task<ModelConfigurationProfile?> GetModelConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ModelConfigurationProfile> SaveModelConfigurationProfileAsync(ModelConfigurationProfile profile, CancellationToken cancellationToken = default);

    Task DeleteModelConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetDefaultModelConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalModel>> ScanLocalModelsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetModelDirectoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HuggingFaceModel>> SearchModelsAsync(string query, CancellationToken cancellationToken = default);
    Task<Guid> StartModelDownloadAsync(ModelDownloadRequest request, CancellationToken cancellationToken = default);
    Task<DownloadStatus?> GetModelDownloadAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ModelStatus> SelectModelAsync(SelectModelRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatSummary>> GetChatSummariesAsync(CancellationToken cancellationToken = default);
    Task<PersistedChat?> AddChatExchangeAsync(Guid id, ChatExchangeRequest request, CancellationToken cancellationToken = default);

    Task<ModelLoadStatus> GetModelStatusAsync(CancellationToken cancellationToken = default);

    Task<ModelLoadStatus> LoadModelAsync(LoadModelRequest request, CancellationToken cancellationToken = default);

    Task<ModelLoadStatus> UnloadModelAsync(CancellationToken cancellationToken = default);

    Task<ModelLoadStatus> UnloadModelAsync(string modelPath, CancellationToken cancellationToken = default);

    Task<OpenVinoDiagnosticsDto> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
    Task<OpenVinoSolveResultDto> SolveDiagnosticAsync(string checkId, CancellationToken cancellationToken = default);
    Task<OpenVinoLoadResultDto> LoadModelAsync(OpenVinoLoadRequest request, CancellationToken cancellationToken = default);
    Task<OpenVinoModelStatusDto> GetOpenVinoModelStatusAsync(CancellationToken cancellationToken = default);
}

public sealed record ChatSummary(Guid Id, string Title, DateTime UpdatedAtUtc, int MessageCount);

public sealed record PersistedChat(Guid Id, string Title, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, IReadOnlyList<PersistedChatMessage> Messages);

public sealed record PersistedChatMessage(string Role, string Content, DateTime CreatedAtUtc, string? ModelPath = null, string? Backend = null, int? TokenCount = null, double? TokensPerSecond = null);

public sealed record LlamaSettings(
    string ModelPath,
    string Backend,
    int GpuLayerCount,
    uint ContextSize,
    IReadOnlyDictionary<string, VulkanDeviceSetting> VulkanDevices,
    LlamaAdvancedSettings? Advanced = null,
    Guid? ConfigurationProfileId = null);

public sealed record LlamaModel(Guid Id, string Name, string Path, long SizeInBytes, DateTime LastWriteTimeUtc, Guid? ConfigurationProfileId = null);

public sealed record ModelConfigurationProfile(
    Guid Id,
    string Name,
    string? Description,
    string ModelPath,
    bool IsDefault,
    int SchemaVersion,
    string ConfigurationJson,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    ConfigurationBackend Backend = ConfigurationBackend.Llama);

public sealed record VulkanDeviceSetting(bool Enabled, float Weight);

public sealed record ModelLoadStatus(
    string? ModelPath,
    string Backend,
    int GpuLayerCount,
    uint ContextSize,
    ulong ModelSizeInBytes,
    int FoundVulkanGpuCount,
    IReadOnlyList<VulkanDeviceStatus> VulkanDevices,
    double? CpuModelBufferMiB,
    string LoadLog,
    IReadOnlyDictionary<string, float> VulkanDeviceWeights,
    bool IsModelLoaded,
    IReadOnlyList<LoadedModelStatus> LoadedModels);

public sealed record VulkanDeviceStatus(string Name, string? Description, int AssignedLayerCount, double? ModelBufferMiB);

public sealed record LoadedModelStatus(
    string ModelPath,
    string Backend,
    int GpuLayerCount,
    uint ContextSize,
    ulong ModelSizeInBytes,
    IReadOnlyList<VulkanDeviceStatus> VulkanDevices,
    double? CpuModelBufferMiB);