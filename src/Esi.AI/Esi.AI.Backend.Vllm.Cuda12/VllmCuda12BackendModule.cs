using Esi.AI.Backend.Abstractions;
using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.AI.Backend.Vllm.Cuda12;

/// <summary>Registers the vLLM CUDA 12 backend as an independent backend module.</summary>
public sealed class VllmCuda12BackendModule : IBackendModule
{
    /// <inheritdoc />
    public BackendVariantDescriptor Descriptor { get; } = new(
        "vllm.cuda12",
        ConfigurationBackend.Vllm,
        "vLLM CUDA 12",
        "vLLM",
        "CUDA12");

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IBackendRuntime, VllmCuda12Runtime>();
    }
}