using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.AI.Backend.Abstractions;

/// <summary>Describes one independently deployable backend variant.</summary>
public sealed record BackendVariantDescriptor(
    string Id,
    ConfigurationBackend Family,
    string DisplayName,
    string RuntimeName,
    string Route);

/// <summary>Registers one backend variant and its runtime implementation with dependency injection.</summary>
public interface IBackendModule
{
    /// <summary>Gets the immutable identity and routing metadata for this backend variant.</summary>
    BackendVariantDescriptor Descriptor { get; }

    /// <summary>Registers the variant's services in the application service collection.</summary>
    /// <param name="services">The service collection that receives the backend services.</param>
    void AddServices(IServiceCollection services);
}