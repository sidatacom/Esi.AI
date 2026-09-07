using System.Text.Json;
using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Configures the time budget for local inference requests.</summary>
public sealed class InferenceTimeoutOptions
{
    /// <summary>Gets or sets the fixed preparation and scheduling budget in seconds.</summary>
    public double BaseSeconds { get; set; } = 120;

    /// <summary>Gets or sets the additional budget in seconds for each supplied tool.</summary>
    public double SecondsPerTool { get; set; } = 1;

    /// <summary>Gets or sets the additional budget in seconds for each estimated prefill token.</summary>
    public double SecondsPerPrefillToken { get; set; } = .01;

    /// <summary>Gets or sets optional backend-specific overrides for the default values.</summary>
    public Dictionary<string, InferenceTimeoutBackendOptions> Backends { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Configures one backend-specific inference timeout override.</summary>
public sealed class InferenceTimeoutBackendOptions
{
    public double BaseSeconds { get; set; } = 120;
    public double SecondsPerTool { get; set; } = 1;
    public double SecondsPerPrefillToken { get; set; } = .01;
}

/// <summary>Contains the measured request inputs used to derive an inference deadline.</summary>
public sealed record InferenceTimeoutDecision(
    string Backend,
    int ToolCount,
    int EstimatedPrefillTokens,
    TimeSpan Timeout);

/// <summary>Calculates a backend-independent deadline without changing the request payload.</summary>
public sealed class InferenceTimeoutPolicy
{
    public static IReadOnlyList<string> BackendNames { get; } = ["Llama", "OpenVINO", "vLLM", "SGLang", "dotLLM"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly InferenceTimeoutOptions options;
    private readonly ApplicationSettingsService? applicationSettings;

    /// <summary>Initializes the policy from application configuration.</summary>
    public InferenceTimeoutPolicy(
        Microsoft.Extensions.Options.IOptions<InferenceTimeoutOptions>? options = null,
        ApplicationSettingsService? applicationSettings = null)
    {
        this.options = options?.Value ?? new InferenceTimeoutOptions();
        this.applicationSettings = applicationSettings;
        Validate(this.options);
    }

    /// <summary>Calculates a deadline from the complete normalized message and tool payload.</summary>
    public InferenceTimeoutDecision Calculate(OpenAiBackendChatRequest request)
        => Calculate(request, FindSettings(CreateDefaultSettings(), request.Backend));

    /// <summary>Reads persisted global settings and calculates the current request deadline.</summary>
    public async Task<InferenceTimeoutDecision> CalculateAsync(OpenAiBackendChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.InferenceTimeout is not null)
            return Calculate(request, request.InferenceTimeout);

        var settings = applicationSettings is null ? CreateDefaultSettings() : await applicationSettings.ReadAsync(cancellationToken).ConfigureAwait(false);
        return Calculate(request, FindSettings(settings, request.Backend));
    }

    private static InferenceTimeoutDecision Calculate(OpenAiBackendChatRequest request, InferenceTimeoutSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);

        var toolCount = request.Tools?.Count ?? 0;
        var promptPayload = JsonSerializer.SerializeToUtf8Bytes(
            new PromptPayload(request.StructuredMessages, request.Tools, request.Options.ToolChoice),
            JsonOptions);
        var estimatedPrefillTokens = Math.Max(1, (promptPayload.Length + 3) / 4);
        var timeoutSeconds = settings.BaseSeconds
            + toolCount * settings.SecondsPerTool
            + estimatedPrefillTokens * settings.SecondsPerPrefillToken;

        return new InferenceTimeoutDecision(
            request.Backend,
            toolCount,
            estimatedPrefillTokens,
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    private ApplicationSettings CreateDefaultSettings() => new(
        BackendNames.Select(backend => options.Backends.TryGetValue(backend, out var configured)
            ? new InferenceTimeoutSettings(backend, configured.BaseSeconds, configured.SecondsPerTool, configured.SecondsPerPrefillToken)
            : new InferenceTimeoutSettings(backend, options.BaseSeconds, options.SecondsPerTool, options.SecondsPerPrefillToken))
        .ToArray());

    private static InferenceTimeoutSettings FindSettings(ApplicationSettings settings, string backend)
    {
        var normalizedBackend = backend is "Vulkan" or "VULKAN" or "CUDA" or "SYCL" or "CPU"
            ? "Llama"
            : backend;
        return settings.InferenceTimeouts.FirstOrDefault(setting => string.Equals(setting.Backend, normalizedBackend, StringComparison.OrdinalIgnoreCase))
            ?? new InferenceTimeoutSettings(normalizedBackend);
    }

    private static void Validate(InferenceTimeoutOptions options)
    {
        if (!double.IsFinite(options.BaseSeconds) || options.BaseSeconds < 0 ||
            !double.IsFinite(options.SecondsPerTool) || options.SecondsPerTool < 0 ||
            !double.IsFinite(options.SecondsPerPrefillToken) || options.SecondsPerPrefillToken < 0 ||
            options.Backends.Any(backend => !double.IsFinite(backend.Value.BaseSeconds) || backend.Value.BaseSeconds < 0 ||
                !double.IsFinite(backend.Value.SecondsPerTool) || backend.Value.SecondsPerTool < 0 ||
                !double.IsFinite(backend.Value.SecondsPerPrefillToken) || backend.Value.SecondsPerPrefillToken < 0))
            throw new ArgumentException("Inference timeout values must be finite and non-negative.", nameof(options));
    }

    private sealed record PromptPayload(
        IReadOnlyList<OpenAiChatMessage> Messages,
        IReadOnlyList<OpenAiToolDefinition>? Tools,
        JsonElement? ToolChoice);
}

/// <summary>Signals that a local inference request exceeded its calculated deadline.</summary>
public sealed class InferenceTimeoutException(InferenceTimeoutDecision decision)
    : TimeoutException($"{decision.Backend} inference exceeded its {decision.Timeout.TotalSeconds:F0}-second deadline for {decision.ToolCount} tools and approximately {decision.EstimatedPrefillTokens} prefill tokens.")
{
    /// <summary>Gets the inputs used to calculate the expired deadline.</summary>
    public InferenceTimeoutDecision Decision { get; } = decision;
}