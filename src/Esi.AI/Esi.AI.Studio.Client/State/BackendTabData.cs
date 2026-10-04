using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>Defines the fixed presentation order for backend tab sections.</summary>
public enum BackendTabSectionKind
{
    BackendRequirements,
    DeviceRouting,
    ModelAndConfiguration,
    LoadConfiguration,
    RuntimeTelemetry
}

/// <summary>Stores the kind, heading, and section-specific data for a backend tab section.</summary>
/// <param name="Kind">The section's position and purpose in the backend layout.</param>
/// <param name="Title">The section heading shown to the user.</param>
/// <param name="Data">The section-specific data.</param>
public sealed record BackendTabSectionData(BackendTabSectionKind Kind, string Title, object? Data);

/// <summary>Stores requirement diagnostics and readiness for a backend.</summary>
/// <param name="Diagnostics">The backend-specific diagnostic result.</param>
/// <param name="IsReady">Whether the backend can load a model.</param>
public sealed record BackendTabRequirementsData(object? Diagnostics, bool IsReady);

/// <summary>Stores the models and saved profiles available to a backend.</summary>
/// <param name="Models">The models available for selection.</param>
/// <param name="Profiles">The configuration profiles available for selection.</param>
/// <param name="SelectedProfileId">The selected profile identifier, if one exists.</param>
public sealed record BackendTabModelConfigurationData(
    IReadOnlyList<BackendModel> Models,
    IReadOnlyList<ModelConfiguration> Profiles,
    Guid? SelectedProfileId);

/// <summary>Stores the current runtime status for a backend tab.</summary>
/// <param name="LoadStatus">The loaded model status, if a model is loaded.</param>
/// <param name="IsLlamaLoading">Whether a LLama model is loading.</param>
/// <param name="IsOpenVinoLoading">Whether an OpenVINO model is loading.</param>
/// <param name="IsPythonLoading">Whether a Python runtime is loading.</param>
public sealed record BackendTabRuntimeTelemetryData(
    ModelLoadStatus? LoadStatus,
    bool IsLlamaLoading,
    bool IsOpenVinoLoading,
    bool IsPythonLoading);

/// <summary>Exposes the shared model-path setting required by backend configuration states.</summary>
public interface IBackendTabConfigurationData
{
    /// <summary>Gets or sets the configured model path.</summary>
    string ModelPath { get; set; }
}

/// <summary>Exposes the common identity, configuration, and ordered sections of a backend tab.</summary>
public interface IBackendTabData
{
    /// <summary>Gets the stable runtime identifier for the backend tab.</summary>
    string RuntimeId { get; }

    /// <summary>Gets the user-facing backend label.</summary>
    string Label { get; }

    /// <summary>Gets the configuration backend represented by this tab.</summary>
    ConfigurationBackend ConfigurationBackend { get; }

    /// <summary>Gets the backend-specific configuration state.</summary>
    IBackendTabConfigurationData Configuration { get; }

    /// <summary>Gets the sections in their shared display order.</summary>
    IReadOnlyList<BackendTabSectionData> Sections { get; }
}

/// <summary>Combines backend configuration and section data into the shared tab contract.</summary>
/// <typeparam name="TConfiguration">The concrete configuration state for the backend.</typeparam>
/// <param name="RuntimeId">The stable runtime identifier for the backend tab.</param>
/// <param name="Label">The user-facing backend label.</param>
/// <param name="ConfigurationBackend">The configuration backend represented by this tab.</param>
/// <param name="Configuration">The backend-specific configuration state.</param>
/// <param name="RequirementsData">The data displayed in the requirements section.</param>
/// <param name="DeviceRoutingData">The backend-specific device routing state.</param>
/// <param name="ModelAndConfigurationData">The model catalog and saved-profile data.</param>
/// <param name="LoadConfigurationData">The backend-specific model loading state.</param>
/// <param name="RuntimeTelemetryData">The current runtime telemetry.</param>
public sealed record BackendTabData<TConfiguration>(
    string RuntimeId,
    string Label,
    ConfigurationBackend ConfigurationBackend,
    TConfiguration Configuration,
    BackendTabRequirementsData RequirementsData,
    TConfiguration DeviceRoutingData,
    BackendTabModelConfigurationData ModelAndConfigurationData,
    TConfiguration LoadConfigurationData,
    BackendTabRuntimeTelemetryData RuntimeTelemetryData) : IBackendTabData
    where TConfiguration : IBackendTabConfigurationData
{
    IBackendTabConfigurationData IBackendTabData.Configuration => Configuration;

    public IReadOnlyList<BackendTabSectionData> Sections { get; } =
    [
        new(BackendTabSectionKind.BackendRequirements, "Backend Requirements", RequirementsData),
        new(BackendTabSectionKind.DeviceRouting, "Device Routing", DeviceRoutingData),
        new(BackendTabSectionKind.ModelAndConfiguration, "Model and configuration", ModelAndConfigurationData),
        new(BackendTabSectionKind.LoadConfiguration, "Load configuration", LoadConfigurationData),
        new(BackendTabSectionKind.RuntimeTelemetry, "Runtime telemetry", RuntimeTelemetryData)
    ];
}