using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Esi.AI.Llm.Models;

namespace Esi.AI.Llm.Router;

/// <summary>
/// Orchestriert die Zuweisung von Aufgaben an die richtigen Agenten und Modelle basierend auf Hardware-Beschränkungen und Modellstärken.
/// </summary>
public class Orchestrator
{
    private readonly ConcurrentDictionary<string, Agent> _agents = new();
    private readonly ConcurrentDictionary<string, ModelAgent> _modelAgents = new();
    private readonly ProviderRouter _providerRouter;

    public Orchestrator(ProviderRouter providerRouter)
    {
        _providerRouter = providerRouter;
    }

    public void RegisterAgent(Agent agent) => _agents[agent.Id] = agent;

    public void RegisterModelAgent(ModelAgent modelAgent) => _modelAgents[modelAgent.ModelId] = modelAgent;

    /// <summary>
    /// Orchestriert einen Request durch die Auswahl des besten Agenten und Modells.
    /// </summary>
    public async Task<ChatCompletionResponse> OrchestrateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Identifiziere den passenden Agenten basierend auf der Beschreibung/Stärken
        var agent = SelectBestAgent(request.Messages.FirstOrDefault()?.Content ?? "");

        // 2. Bestimme das primäre Modell für diesen Agenten
        var primaryModelId = agent?.PrimaryModelId ?? "nemotron";

        // 3. Hardware-Check und Routing
        // Gemma-4 ist auf Nvidia dediziert. Nemotron/Qwen auf Intel.
        // Wenn der Agent Gemma-4 nutzen soll, ist das okay.
        // Wenn er Nemotron nutzen soll, prüfen wir, ob es auf Intel läuft.

        var modelAgent = _modelAgents.GetValueOrDefault(primaryModelId);
        
        // Logik: Wenn Nemotron nicht ausreicht, könnte Gemma-4 (als Orchestrator) entscheiden, Qwen zu nutzen.
        // Da Gemma-4 auf Nvidia läuft, können wir es als "Fallback" oder "Decision Maker" nutzen.
        
        // Hier vereinfacht: Wenn das primäre Modell Nemotron ist und wir eine komplexe Aufgabe haben, 
        // könnten wir prüfen, ob Qwen besser wäre.
        
        string targetModelId = primaryModelId;
        if (primaryModelId == "nemotron" && IsComplexTask(request))
        {
            // Hier könnte Gemma-4 entscheiden. Da wir hier nur die Logik beschreiben:
            // Wenn Nemotron nicht die beste Wahl ist, wechsle zu Qwen.
            targetModelId = "qwen";
        }

        // 4. Führe den Request über den ProviderRouter aus
        var (deployment, provider) = _providerRouter.SelectDeployment(RoutingStrategy.LeastBusy, cancellationToken: cancellationToken);

        if (provider == null)
        {
            return new ChatCompletionResponse { Id = Guid.NewGuid().ToString(), Object = "chat.completion", Choices = new List<ChatCompletionResponse.Choice>(), Error = new ProviderError { Message = "No provider found" } };
        }

        // Wir müssen sicherstellen, dass der Request das richtige Modell verwendet
        // Da der ProviderRouter das Deployment wählt, müssen wir sicherstellen, dass das Deployment das richtige Modell hat.
        // In dieser Architektur scheint das Deployment mit dem Modell verknüpft zu sein.
        
        // Wir müssen den Request anpassen, damit der Provider das richtige Modell nutzt.
        var adjustedRequest = new ChatCompletionRequest
        {
            Model = targetModelId,
            Messages = request.Messages,
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            Stream = request.Stream
        };

        return await provider.CompleteAsync(adjustedRequest, cancellationToken: cancellationToken);
    }

    private Agent? SelectBestAgent(string query)
    {
        // Einfache Auswahl: Suche Agent, dessen Beschreibung am besten passt
        return _agents.Values
            .OrderByDescending(a => a.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .FirstOrDefault();
    }

    private bool IsComplexTask(ChatCompletionRequest request)
    {
        // Einfache Heuristik für Komplexität
        return request.Messages.Any(m => m.Content?.Length > 500);
    }
}
