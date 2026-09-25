using Esi.AI.Backend.Abstractions;
using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.AI.Backend.Vllm.Xpu;

/// <summary>Registers the standalone vLLM Intel XPU backend.</summary>
public sealed class VllmXpuBackendModule : IBackendModule
{
    /// <inheritdoc />
    public BackendVariantDescriptor Descriptor { get; } = new(
        "vllm.xpu",
        ConfigurationBackend.Vllm,
        "vLLM Intel XPU",
        "vLLM",
        "XPU");

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IBackendRuntime, VllmXpuRuntime>();
    }
}
