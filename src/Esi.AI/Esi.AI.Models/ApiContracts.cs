namespace Esi.AI.Models;

public enum ConfigurationBackend
{
    Llama,
    OpenVino,
    Vllm,
    Sglang,
    DotLlm
}

public sealed record BackendModel(
    string Name,
    string Path,
    long SizeInBytes,
    DateTime LastWriteTimeUtc,
    ConfigurationBackend Backend,
    Guid? ConfigurationProfileId = null);

/// <summary>Describes the prerequisite checks for one inference backend.</summary>
public sealed record BackendPrerequisiteDiagnostics(
    ConfigurationBackend Backend,
    string BackendName,
    bool IsReady,
    IReadOnlyList<BackendPrerequisiteCheck> Checks,
    string? Error = null);

/// <summary>Describes one backend prerequisite and whether it can be repaired.</summary>
public sealed record BackendPrerequisiteCheck(
    string Id,
    string Name,
    bool IsAvailable,
    string Detail,
    bool CanSolve,
    bool IsOptional = false);

/// <summary>Contains the result and output of a backend preparation action.</summary>
public sealed record BackendPrerequisiteSolveResult(
    bool Succeeded,
    string Message,
    string Output);

public sealed record LoadModelRequest(
    string ModelPath,
    string Backend,
    int GpuLayerCount,
    uint ContextSize,
    IReadOnlyDictionary<string, float> VulkanDeviceWeights,
    LlamaAdvancedSettings? AdvancedSettings)
{
    public LlamaAdvancedSettings Advanced { get; } = AdvancedSettings ?? new();
}

public sealed record ChatRequest(IReadOnlyList<ChatMessageRequest> Messages, string? SystemPrompt = null);

public sealed record ChatMessageRequest(string Role, string Content);

public sealed record ChatResponse(string Content);

public sealed record CreateChatRequest(string? Title = null);

public sealed record ChatExchangeRequest(string Content, string? ModelPath = null, string? Backend = null);

public sealed record ModelDownloadRequest(string ModelId, string? FileName = null, string Library = "gguf");

public sealed record ModelDownloadOption(string FileName, int FileCount, long? SizeInBytes = null)
{
    public string Label
    {
        get
        {
            var parts = new List<string> { FileName };
            if (FileCount > 1)
                parts.Add($"{FileCount} Dateien");
            if (SizeInBytes is long size)
                parts.Add(FormatSize(size));
            return string.Join(" · ", parts);
        }
    }

    private static string FormatSize(long size)
    {
        const double unit = 1024;
        var value = (double)size;
        var units = new[] { "B", "KiB", "MiB", "GiB", "TiB" };
        var index = 0;
        while (value >= unit && index < units.Length - 1)
        {
            value /= unit;
            index++;
        }

        return $"{value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} {units[index]}";
    }
}

public sealed record HuggingFaceSearchRequest(
    string Query,
    string Library = "gguf",
    string Task = "",
    string ParameterRange = "",
    string Language = "",
    string License = "",
    string Hardware = "",
    string Other = "",
    string InferenceProvider = "",
    bool BaseOnly = false,
    bool InferenceAvailable = false,
    string Sort = "downloads");

public sealed record SelectModelRequest(string Path);

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

public sealed record OpenVinoSettings(
    string ModelPath,
    string Device,
    IReadOnlyDictionary<string, OpenVinoDeviceSetting> Devices,
    string CacheDirectory = "",
    int MaxNewTokens = 512,
    float Temperature = .7f,
    float TopP = .9f,
    bool DoSample = true,
    int TopK = 50,
    float RepetitionPenalty = 1.2f,
    Guid? ConfigurationProfileId = null,
    OpenVinoNpuSettings? Npu = null);

public sealed record OpenVinoDeviceSetting(bool Enabled, float Weight);

public sealed record OpenVinoLoadRequest(
    string ModelPath,
    string Device,
    string CacheDirectory = "",
    int MaxNewTokens = 512,
    float Temperature = .7f,
    float TopP = .9f,
    bool DoSample = true,
    OpenVinoNpuSettings? Npu = null,
    int TopK = 50,
    float RepetitionPenalty = 1.2f);

public sealed record PythonInferenceLoadRequest(
    string ModelPath,
    ConfigurationBackend Backend,
    string PythonExecutable = "python3",
    string? WorkingDirectory = null,
    int Port = 8000,
    int? GpuMemoryUtilization = null,
    uint MaxModelLength = 2048,
    int TensorParallelSize = 1,
    bool TrustRemoteCode = true,
    TimeSpan? StartupTimeout = null,
    uint MaxTokens = 512,
    float Temperature = .7f,
    float TopP = .9f,
    bool EnforceEager = false);

public sealed record DotLlmLoadRequest(
    string ModelPath,
    string Device = "cpu",
    int? Threads = null);

public sealed record OpenVinoNpuSettings(
    int MaxPromptLength = 1024,
    int MinResponseLength = 128,
    string PrefillHint = "DYNAMIC",
    string GenerateHint = "FAST_COMPILE");

public sealed record OpenVinoLoadResultDto(bool Succeeded, string Message, string Device);

public sealed record OpenVinoModelStatusDto(
    string? ModelPath,
    string? Device,
    bool IsModelLoaded,
    ulong ModelSizeInBytes,
    string LoadLog);

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
    ConfigurationBackend Backend,
    string Runtime,
    int GpuLayerCount,
    uint ContextSize,
    ulong ModelSizeInBytes,
    IReadOnlyList<VulkanDeviceStatus> VulkanDevices,
    double? CpuModelBufferMiB,
    string LoadLog = "");
