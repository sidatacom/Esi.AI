namespace Esi.AI.Llm.Providers;

/// <summary>
/// Resolves providers for configured deployments without performing network calls.
/// </summary>
public interface IProviderRegistry
{
    /// <summary>Registers a provider for a deployment.</summary>
    void Register(DeploymentConfig deployment, IChatCompletionProvider provider);

    /// <summary>Resolves an active deployment by model and optional provider.</summary>
    (DeploymentConfig Deployment, IChatCompletionProvider Provider) Resolve(string model, string? provider = null);
}

/// <summary>
/// In-memory provider registry used by the router and unit tests.
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly List<(DeploymentConfig Deployment, IChatCompletionProvider Provider)> _entries = [];

    /// <inheritdoc />
    public void Register(DeploymentConfig deployment, IChatCompletionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployment.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployment.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployment.Provider);

        _entries.RemoveAll(entry => entry.Deployment.Name.Equals(deployment.Name, StringComparison.OrdinalIgnoreCase));
        _entries.Add((deployment, provider));
    }

    /// <inheritdoc />
    public (DeploymentConfig Deployment, IChatCompletionProvider Provider) Resolve(string model, string? provider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var match = _entries.FirstOrDefault(entry =>
            entry.Deployment.IsActive &&
            entry.Deployment.Model.Equals(model, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(provider) || entry.Deployment.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)));

        if (match.Provider is null)
            throw new KeyNotFoundException($"No active provider deployment found for model '{model}'.");

        return match;
    }
}