using Esi.AI.Backend.Abstractions;
using Esi.AI.Backend.Llama.Cuda12;
using Esi.AI.Backend.Llama.Sycl;
using Esi.AI.Backend.Llama.Vulkan;
using Esi.AI.Backend.OpenVino;
using Esi.AI.Backend.Vllm.Cuda12;
using Esi.AI.Backend.Vllm.Xpu;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.AI.Studio.Services;

/// <summary>Registers the backend modules shipped with Esi.AI Studio.</summary>
public static class BackendModuleRegistrationExtensions
{
    /// <summary>Registers every packaged inference backend and the shared runtime resolver.</summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The original service collection.</returns>
    public static IServiceCollection AddEsiAiBackendModules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBackendModule(new LlamaCuda12BackendModule());
        services.AddBackendModule(new LlamaSyclBackendModule());
        services.AddBackendModule(new LlamaVulkanBackendModule());
        services.AddBackendModule(new OpenVinoBackendModule());
        services.AddBackendModule(new VllmCuda12BackendModule());
        services.AddBackendModule(new VllmXpuBackendModule());
        return services;
    }
}