using Esi.AI.Backend.Abstractions;
using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.AI.Backend.OpenVino;

/// <summary>Registers the OpenVINO inference backend.</summary>
public sealed class OpenVinoBackendModule : IBackendModule
{
    /// <inheritdoc />
    public BackendVariantDescriptor Descriptor { get; } = new(
        "openvino",
        ConfigurationBackend.OpenVino,
        "OpenVINO",
        "OpenVINO",
        "OpenVINO");

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IBackendRuntime, OpenVinoRuntime>();
    }
}