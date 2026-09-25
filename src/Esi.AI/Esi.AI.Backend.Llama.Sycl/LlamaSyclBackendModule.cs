using Esi.AI.Backend.Abstractions;
using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.AI.Backend.Llama.Sycl;

/// <summary>Registers the LLamaSharp SYCL backend as an independent backend module.</summary>
public sealed class LlamaSyclBackendModule : IBackendModule
{
    /// <summary>Gets the stable identity and display metadata of the SYCL backend.</summary>
    public BackendVariantDescriptor Descriptor { get; } = new(
        "llama.sycl",
        ConfigurationBackend.Llama,
        "Llama.cpp SYCL",
        "LLamaSharp SYCL",
        "SYCL");

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IBackendRuntime, LlamaSyclRuntime>();
    }
}