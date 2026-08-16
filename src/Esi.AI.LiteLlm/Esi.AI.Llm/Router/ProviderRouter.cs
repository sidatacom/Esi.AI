using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Esi.AI.Llm.Router;

/// <summary>
/// Zentrale Provider-Router mit Deployment-Management und Routing-Strategien.
/// Wählt anhand der Konfiguration den passenden Provider für einen Request aus.
/// </summary>
public class ProviderRouter
{
    private readonly ConcurrentDictionary<string, DeploymentConfig> _deployments = new();
    private readonly ConcurrentDictionary<string, IChatCompletionProvider> _providers = new();
    private int _roundRobinIndex;

    /// <summary>
    /// Fügt ein Deployment zur Verfügung.
    /// </summary>
    /// <param name="deployment">Deployment-Konfiguration.</param>
    /// <param name="provider">Provider-Instanz.</param>
    public void RegisterDeployment(DeploymentConfig deployment, IChatCompletionProvider provider)
    {
        _deployments[deployment.Name] = deployment;
        _providers[deployment.Name] = provider;
    }

    /// <summary>
    /// Entfernt ein Deployment (z.B. bei Fehlern).
    /// </summary>
    /// <param name="deploymentName">Name des zu entfernenden Deployments.</param>
    public void UnregisterDeployment(string deploymentName)
    {
        _deployments.TryRemove(deploymentName, out _);
        _providers.TryRemove(deploymentName, out _);
    }

    /// <summary>
    /// Wählt ein Deployment anhand der Routing-Strategie aus.
    /// </summary>
    /// <param name="strategy">Die zu verwendende Routing-Strategie.</param>
    /// <param name="budgetConfig">Aktuelles Budget (optional).</param>
    /// <param name="cancellationToken">Abbrech-Token.</param>
    /// <returns>Gewähltes Deployment, oder null wenn kein passendes gefunden wurde.</returns>
    public (DeploymentConfig? Deployment, IChatCompletionProvider? Provider) SelectDeployment(
        RoutingStrategy strategy,
        BudgetConfig? budgetConfig = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var activeDeployments = _deployments.Values.Where(d => d.IsActive).ToList();

        if (activeDeployments.Count == 0)
            return (null, null);

        return strategy switch
        {
            RoutingStrategy.RoundRobin => SelectRoundRobin(activeDeployments),
            RoutingStrategy.LowestLatency => SelectLowestLatency(activeDeployments),
            RoutingStrategy.LowestCost => SelectLowestCost(activeDeployments, budgetConfig),
            RoutingStrategy.LeastBusy => SelectLeastBusy(activeDeployments),
            _ => SelectRoundRobin(activeDeployments)
        };
    }

    private (DeploymentConfig? Deployment, IChatCompletionProvider? Provider) SelectRoundRobin(List<DeploymentConfig> deployments)
    {
        if (deployments.Count == 0)
            return (null, null);

        var deployment = deployments[_roundRobinIndex % deployments.Count];
        _roundRobinIndex++;

        return (deployment, _providers.TryGetValue(deployment.Name, out var provider) ? provider : null);
    }

    private (DeploymentConfig? Deployment, IChatCompletionProvider? Provider) SelectLowestLatency(List<DeploymentConfig> deployments)
    {
        DeploymentConfig? best = null;
        IChatCompletionProvider? bestProvider = null;
        int bestLatency = int.MaxValue;

        foreach (var deployment in deployments)
        {
            if (_providers.TryGetValue(deployment.Name, out var provider) &&
                _deployments.TryGetValue(deployment.Name, out var metrics) &&
                metrics.AverageLatencyMs > 0 && metrics.AverageLatencyMs < bestLatency)
            {
                best = deployment;
                bestProvider = provider;
                bestLatency = (int)best.AverageLatencyMs;
            }
        }

        return (best, bestProvider);
    }

    private (DeploymentConfig? Deployment, IChatCompletionProvider? Provider) SelectLowestCost(
        List<DeploymentConfig> deployments,
        BudgetConfig? budgetConfig)
    {
        // Einfache Kosten-Schätzung basierend auf Modell-Typ
        DeploymentConfig? best = null;
        IChatCompletionProvider? bestProvider = null;
        double? bestCostPerToken = null;

        foreach (var deployment in deployments)
        {
            if (_providers.TryGetValue(deployment.Name, out var provider))
            {
                // Grobe Kosten-Schätzung: einfach das erste verfügbare nehmen
                // In einer echten Implementierung würden Sie hier tatsächliche Preisdaten verwenden
                best = deployment;
                bestProvider = provider;
                bestCostPerToken = 0.001; // Platzhalter
                break;
            }
        }

        return (best, bestProvider);
    }

    private (DeploymentConfig? Deployment, IChatCompletionProvider? Provider) SelectLeastBusy(List<DeploymentConfig> deployments)
    {
        DeploymentConfig? best = null;
        IChatCompletionProvider? bestProvider = null;
        int minActive = int.MaxValue;

        foreach (var deployment in deployments)
        {
            if (_deployments.TryGetValue(deployment.Name, out var metrics) &&
                metrics.ActiveRequests < minActive)
            {
                best = deployment;
                bestProvider = _providers.TryGetValue(deployment.Name, out var p) ? p : null;
                minActive = metrics.ActiveRequests;
            }
        }

        return (best, bestProvider);
    }

    /// <summary>
    /// Führt einen Request mit Retries und der angegebenen Strategie aus.
    /// </summary>
    /// <param name="request">Die Chat-Completion-Anfrage.</param>
    /// <param name="strategy">Die Routing-Strategie.</param>
    /// <param name="maxRetries">Maximale Anzahl an Retries.</param>
    /// <param name="delayMs">Verzögerung zwischen Retries in ms.</param>
    /// <param name="cancellationToken">Abbrech-Token.</param>
    /// <returns>ProviderResult mit der Antwort.</returns>
    public async Task<ProviderResult> CompleteAsync(
        ChatCompletionRequest request,
        RoutingStrategy strategy = RoutingStrategy.RoundRobin,
        int maxRetries = 3,
        int delayMs = 1000,
        BudgetConfig? budgetConfig = null,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (deployment, provider) = SelectDeployment(strategy, budgetConfig, cancellationToken);

            if (provider == null)
            {
                return new ProviderResult
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = string.Empty,
                    FinishReason = "error",
                    Error = new ProviderResult.ErrorInfo
                    {
                        Reason = "No available provider",
                        Code = "no_provider_available",
                        StatusCode = 503,
                        IsRetryable = true
                    }
                };
            }

            try
            {
                var result = await provider.CompleteAsync(request, cancellationToken);
                if (result.Error == null || !result.Error.IsRetryable)
                    return result;

                // Fehler ist retry-fähig, warte und wiederhole
                if (attempt < maxRetries)
                {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
            {
                // Timeout oder Abbruch - retry wenn möglich
                if (attempt < maxRetries)
                {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    return new ProviderResult
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = string.Empty,
                        FinishReason = "error",
                        Error = new ProviderResult.ErrorInfo
                        {
                            Reason = "Request timed out or was aborted",
                            Code = "timeout",
                            StatusCode = 504,
                            IsRetryable = true
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                // Unerwarteter Fehler - retry wenn möglich
                if (attempt < maxRetries)
                {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    return new ProviderResult
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = string.Empty,
                        FinishReason = "error",
                        Error = new ProviderResult.ErrorInfo
                        {
                            Reason = $"Provider error: {ex.Message}",
                            Code = "provider_error",
                            StatusCode = 500,
                            IsRetryable = true
                        }
                    };
                }
            }
        }

        // Sollte nicht erreicht werden, aber als Fallback
        return new ProviderResult
        {
            Id = Guid.NewGuid().ToString(),
            Content = string.Empty,
            FinishReason = "error",
            Error = new ProviderResult.ErrorInfo
            {
                Reason = "Max retries exceeded",
                Code = "max_retries_exceeded",
                StatusCode = 500,
                IsRetryable = false
            }
        };
    }
}