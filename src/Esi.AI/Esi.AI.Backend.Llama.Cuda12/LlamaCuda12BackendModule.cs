using Esi.AI.Backend.Abstractions;
using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.AI.Backend.Llama.Cuda12;

/// <summary>Registers the LLamaSharp CUDA 12 backend as an independent backend module.</summary>
public sealed class LlamaCuda12BackendModule : IBackendModule
{
    /// <summary>Gets the stable identity and display metadata of the CUDA 12 backend.</summary>
    public BackendVariantDescriptor Descriptor { get; } = new(
        "llama.cuda12",
        ConfigurationBackend.Llama,
        "Llama.cpp CUDA 12",
        "LLamaSharp CUDA 12",
        "CUDA");

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IBackendRuntime, LlamaCuda12Runtime>();
    }
}