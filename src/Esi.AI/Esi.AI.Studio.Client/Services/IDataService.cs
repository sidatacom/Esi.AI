namespace Esi.AI.Studio.Client.Services;

public interface IDataService
{
    Task<LlamaSettings?> GetLlamaSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveLlamaSettingsAsync(LlamaSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LlamaModel>> GetLlamaModelsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LlamaModel>> ScanLlamaModelsAsync(CancellationToken cancellationToken = default);

    Task SyncLlamaModelsAsync(IReadOnlyList<LlamaModel> models, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LlamaConfigurationProfile>> GetLlamaConfigurationProfilesAsync(CancellationToken cancellationToken = default);

    Task<LlamaConfigurationProfile?> GetLlamaConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default);

    Task<LlamaConfigurationProfile> SaveLlamaConfigurationProfileAsync(LlamaConfigurationProfile profile, CancellationToken cancellationToken = default);

    Task DeleteLlamaConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetDefaultLlamaConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default);

}

public interface ILlamaControlService
{
    Task<ModelLoadStatus> GetModelStatusAsync(CancellationToken cancellationToken = default);

    Task<ModelLoadStatus> LoadModelAsync(LoadModelRequest request, CancellationToken cancellationToken = default);

    Task<ModelLoadStatus> UnloadModelAsync(CancellationToken cancellationToken = default);

    Task<ModelLoadStatus> UnloadModelAsync(string modelPath, CancellationToken cancellationToken = default);
}

public sealed record ChatSummary(Guid Id, string Title, DateTime UpdatedAtUtc, int MessageCount);

public sealed record PersistedChat(Guid Id, string Title, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, IReadOnlyList<PersistedChatMessage> Messages);

public sealed record PersistedChatMessage(string Role, string Content, DateTime CreatedAtUtc, string? ModelPath = null, int? TokenCount = null, double? TokensPerSecond = null);

public sealed record LlamaSettings(
    string ModelPath,
    string Backend,
    int GpuLayerCount,
    uint ContextSize,
    IReadOnlyDictionary<string, VulkanDeviceSetting> VulkanDevices,
    LlamaAdvancedSettings? Advanced = null,
    Guid? ConfigurationProfileId = null);

public sealed record LlamaModel(Guid Id, string Name, string Path, long SizeInBytes, DateTime LastWriteTimeUtc);

public sealed record LlamaConfigurationProfile(
    Guid Id,
    string Name,
    string? Description,
    string ModelPath,
    bool IsDefault,
    int SchemaVersion,
    string ConfigurationJson,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record VulkanDeviceSetting(bool Enabled, float Weight);

public sealed record LlamaAdvancedSettings(
    int MainGpu = 0,
    uint SeqMax = 1,
    uint RecurrentRollbackSnapshots = 0,
    bool UseMemorymap = true,
    bool UseDirectIO = false,
    bool UseMemoryLock = false,
    int? Threads = null,
    int? BatchThreads = null,
    uint BatchSize = 512,
    uint UBatchSize = 512,
    bool Embeddings = false,
    bool NoKqvOffload = false,
    bool? FlashAttention = null,
    bool VocabOnly = false,
    bool? OpOffload = null,
    bool? SwaFull = null,
    bool? KVUnified = null,
    float? RopeFrequencyBase = null,
    float? RopeFrequencyScale = null,
    float? YarnExtrapolationFactor = null,
    float? YarnAttentionFactor = null,
    float? YarnBetaFast = null,
    float? YarnBetaSlow = null,
    uint? YarnOriginalContext = null,
    string ContextType = "Default",
    string? TypeK = null,
    string? TypeV = null,
    string PoolingType = "Unspecified",
    string AttentionType = "Unspecified",
    string? YarnScalingType = null,
    bool CheckTensors = false,
    float Temperature = .75f,
    int TopK = 40,
    float TopP = .9f,
    float MinP = .1f,
    float RepeatPenalty = 1f,
    float FrequencyPenalty = 0,
    float PresencePenalty = 0,
    int PenaltyCount = 64,
    int MaxTokens = -1,
    int TokensKeep = 0,
    uint Seed = 0,
    bool DecodeSpecialTokens = false);

public sealed record LoadModelRequest(
    string ModelPath,
    string Backend,
    int GpuLayerCount,
    uint ContextSize,
    IReadOnlyDictionary<string, float> VulkanDeviceWeights,
    LlamaAdvancedSettings Advanced);

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