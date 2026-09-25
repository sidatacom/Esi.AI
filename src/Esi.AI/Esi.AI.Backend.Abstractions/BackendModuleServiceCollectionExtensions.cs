using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Esi.AI.Backend.Abstractions;

/// <summary>Provides registration helpers for independently packaged backend modules.</summary>
public static class BackendModuleServiceCollectionExtensions
{
    /// <summary>Registers a backend module and the services supplied by its assembly.</summary>
    /// <param name="services">The service collection to add the module to.</param>
    /// <param name="module">The backend module to register.</param>
    /// <returns>The original service collection.</returns>
    /// <exception cref="ArgumentNullException">The service collection or module is <see langword="null" />.</exception>
    public static IServiceCollection AddBackendModule(this IServiceCollection services, IBackendModule module)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(module.Descriptor.Id);

        module.AddServices(services);
        services.TryAddSingleton<IBackendRuntimeResolver>(provider =>
            new BackendRuntimeResolver(provider.GetServices<IBackendRuntime>()));
        services.AddSingleton(module);
        services.AddSingleton<IBackendModule>(module);
        return services;
    }
}