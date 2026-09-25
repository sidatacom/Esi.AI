using Esi.AI.Backend.Abstractions;
using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.AI.Backend.Llama.Vulkan;

/// <summary>Registers the LLamaSharp Vulkan backend as an independent backend module.</summary>
public sealed class LlamaVulkanBackendModule : IBackendModule
{
    /// <summary>Gets the stable identity and display metadata of the Vulkan backend.</summary>
    public BackendVariantDescriptor Descriptor { get; } = new(
        "llama.vulkan",
        ConfigurationBackend.Llama,
        "Llama.cpp Vulkan",
        "LLamaSharp Vulkan",
        "Vulkan");

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IBackendRuntime, LlamaVulkanRuntime>();
    }
}