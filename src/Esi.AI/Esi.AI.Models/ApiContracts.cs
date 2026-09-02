using System.Text.Json;
using System.Text.Json.Serialization;

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
    Guid? ConfigurationId = null,
    IReadOnlyList<ConfigurationBackend>? CompatibleBackends = null);

public sealed record ModelSettings(
    string ModelPath,
    ConfigurationBackend Backend,
    string ConfigurationJson,
    Guid? ConfigurationId = null);

public sealed record Model(
    Guid Id,
    string Name,
    string Path,
    long SizeInBytes,
    DateTime LastWriteTimeUtc,
    Guid? ConfigurationId = null);

public sealed record ModelConfiguration(
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

/// <summary>Contains the cached prerequisite state for all known backend/vendor routes.</summary>
public sealed record BackendRequirementState(
    IReadOnlyList<BackendRequirementSnapshot> Entries,
    DateTimeOffset UpdatedAtUtc,
    bool IsRefreshing = false);

/// <summary>Contains prerequisite diagnostics for one backend and vendor route.</summary>
public sealed record BackendRequirementSnapshot(
    ConfigurationBackend Backend,
    string Vendor,
    IReadOnlyList<string> Devices,
    BackendPrerequisiteDiagnostics Diagnostics);

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

public sealed record ChatMessage(string Role, string Content);

/// <summary>Contains sampling and stopping options shared by every local inference backend.</summary>
public sealed record ChatGenerationOptions(
    int MaxTokens = 128,
    float Temperature = .7f,
    float TopP = .9f,
    int TopK = 50,
    float MinP = .1f,
    float RepetitionPenalty = 1f,
    float FrequencyPenalty = 0,
    float PresencePenalty = 0,
    int PenaltyCount = 64,
    int? Seed = null,
    IReadOnlyList<string>? StopSequences = null);

public sealed record ChatResponse(string Content);

public sealed record OpenAiChatRequest(
    string? Model,
    IReadOnlyList<OpenAiChatMessage>? Messages,
    bool Stream = false)
{
    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("max_completion_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxCompletionTokens { get; init; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; init; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? TopP { get; init; }

    [JsonPropertyName("top_k")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TopK { get; init; }

    [JsonPropertyName("min_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? MinP { get; init; }

    [JsonPropertyName("repetition_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? RepetitionPenalty { get; init; }

    [JsonPropertyName("frequency_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? FrequencyPenalty { get; init; }

    [JsonPropertyName("presence_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? PresencePenalty { get; init; }

    [JsonPropertyName("seed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seed { get; init; }

    [JsonPropertyName("stop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Stop { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OpenAiToolDefinition>? Tools { get; init; }

    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ToolChoice { get; init; }

    [JsonPropertyName("stream_options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAiStreamOptions? StreamOptions { get; init; }

    [JsonPropertyName("response_format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ResponseFormat { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>Configures the optional OmniRoute OpenAI-compatible upstream.</summary>
public sealed class OmniRouteOptions
{
    /// <summary>Gets or sets a value indicating whether OmniRoute should be used as an upstream.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the base URL of the OmniRoute server.</summary>
    public string BaseUrl { get; set; } = "http://localhost:20128";

    /// <summary>Gets or sets the optional bearer token used for OmniRoute requests.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets a value indicating whether the incoming bearer token may be forwarded.</summary>
    public bool ForwardAuthorizationHeader { get; set; }

    /// <summary>Gets or sets the request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed record OpenAiChatMessage(
    string Role,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Content = null,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null);

public sealed record OpenAiToolDefinition(
    string Type,
    OpenAiToolFunction Function);

public sealed record OpenAiToolFunction(
    string Name,
    string? Description = null,
    JsonElement? Parameters = null);

public sealed record OpenAiStreamOptions(
    [property: JsonPropertyName("include_usage")] bool IncludeUsage = false);

public sealed record OpenAiToolCall(
    string Id,
    string Type,
    OpenAiToolCallFunction Function);

public sealed record OpenAiToolCallFunction(
    string Name,
    string Arguments);

public sealed record OpenAiModelListResponse(string Object, IReadOnlyList<OpenAiModel> Data);

/// <summary>Describes the capabilities supported by an individual language model.</summary>
public sealed record ModelCapabilities(
    bool ToolCalling = false,
    bool ImageInput = false,
    bool AgentMode = false,
    bool Thinking = false);

public sealed record OpenAiModel(
    string Id,
    string Object,
    long Created,
    [property: JsonPropertyName("owned_by")] string OwnedBy,
    string? Name = null,
    ModelCapabilities? Capabilities = null,
    bool Loaded = false);

public sealed record OpenAiChatCompletionResponse(
    string Id,
    string Object,
    long Created,
    string Model,
    IReadOnlyList<OpenAiChatCompletionChoice> Choices,
    OpenAiUsage? Usage = null);

public sealed record OpenAiChatCompletionChoice(
    int Index,
    OpenAiChatMessage Message,
    [property: JsonPropertyName("finish_reason")] string FinishReason);

public sealed record OpenAiChatCompletionChunk(
    string Id,
    string Object,
    long Created,
    string Model,
    IReadOnlyList<OpenAiChatCompletionChunkChoice> Choices,
    OpenAiUsage? Usage = null);

public sealed record OpenAiChatCompletionChunkChoice(
    int Index,
    OpenAiChatCompletionDelta Delta,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

public sealed record OpenAiChatCompletionDelta(string? Role = null, string? Content = null);

public sealed record OpenAiUsage(
    [property: JsonPropertyName("prompt_tokens")] int? PromptTokens = null,
    [property: JsonPropertyName("completion_tokens")] int? CompletionTokens = null,
    [property: JsonPropertyName("total_tokens")] int? TotalTokens = null,
    [property: JsonPropertyName("tokens_per_second")] double? TokensPerSecond = null);

public sealed record OpenAiErrorResponse(OpenAiError Error);

public sealed record OpenAiError(string Message, string Type);

public sealed record CreateChatRequest(string? Title = null);

public sealed record ChatExchangeRequest(string Content, string? ModelPath = null, string? Backend = null);

public sealed record ChatSummary(Guid Id, string Title, DateTime UpdatedAtUtc, int MessageCount);

public sealed record PersistedChat(Guid Id, string Title, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, IReadOnlyList<PersistedChatMessage> Messages);

public sealed record PersistedChatMessage(string Role, string Content, DateTime CreatedAtUtc, string? ModelPath = null, string? Backend = null, int? TokenCount = null, double? TokensPerSecond = null);

public sealed record ChatStreamUpdate(Guid ChatId, string Delta, bool IsCompleted = false, PersistedChat? Chat = null);

public sealed record ModelDownloadRequest(string ModelId, string? FileName = null, string Library = "gguf");

public sealed record ModelDownloadOption(string FileName, int FileCount, long? SizeInBytes = null)
{
    public string Label
    {
        get
        {
            var parts = new List<string> { string.IsNullOrWhiteSpace(FileName) ? "Repository" : FileName };
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
    IReadOnlyList<string>? Libraries = null,
    IReadOnlyList<string>? Tasks = null,
    IReadOnlyList<string>? ParameterRanges = null,
    IReadOnlyList<string>? Languages = null,
    IReadOnlyList<string>? Licenses = null,
    IReadOnlyList<string>? Hardware = null,
    IReadOnlyList<string>? Other = null,
    IReadOnlyList<string>? InferenceProviders = null,
    bool BaseOnly = false,
    bool InferenceAvailable = false,
    string Sort = "downloads",
    int? VramBudgetGiB = null,
    uint? ContextLength = null);

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

public sealed record OpenVinoDeviceSetting(bool Enabled, float Weight);

public sealed record VulkanDeviceSetting(bool Enabled, float Weight);

public sealed record OpenVinoLoadRequest(
    string ModelPath,
    string Device,
    string CacheDirectory = "",
    int MaxNewTokens = 128,
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
    bool EnforceEager = false,
    string Device = "cuda:0",
    IReadOnlyList<string>? Devices = null);

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

public sealed record VulkanDeviceStatus(
    string Name,
    string? Description,
    int AssignedLayerCount,
    double? ModelBufferMiB,
    string? Vendor = null,
    string? Driver = null,
    double? MemoryCapacityMiB = null);

public sealed record LoadedModelStatus(
    string ModelPath,
    ConfigurationBackend Backend,
    string Runtime,
    int GpuLayerCount,
    uint ContextSize,
    ulong ModelSizeInBytes,
    IReadOnlyList<VulkanDeviceStatus> VulkanDevices,
    double? CpuModelBufferMiB,
    string LoadLog = "",
    bool IsLoading = false);
