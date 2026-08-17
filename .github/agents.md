# Agent Orchestration

## Overview

Agent orchestration defines how multiple AI agents coordinate, delegate, and collaborate to accomplish complex tasks. This document serves as the entry point for understanding agent routing, delegation patterns, and workflow management within the Esi.AI ecosystem.

## Core Concepts

### Agent Types

| Type | Description | Use Case |
|------|-------------|----------|
| **Specialist** | Focused on a specific domain (e.g., code review, documentation, testing) | Domain-specific tasks |
| **Coordinator** | Orchestrates multiple agents, assigns tasks, tracks progress | Complex multi-step workflows |
| **Router** | Determines which agent should handle a request based on intent | Request routing |
| **Executor** | Performs concrete actions (code generation, testing, deployment) | Task execution |
| **Auditor** | Reviews agent outputs, enforces policies, ensures compliance | Quality assurance |

### Orchestration Patterns

1. **Hierarchical**: Coordinator → Specialist agents
2. **Peer-to-Peer**: Agents communicate directly with peer agents
3. **Pipeline**: Sequential agent processing with intermediate validation
4. **Fan-out/Fan-in**: One agent branches to multiple, results converge
5. **Feedback Loop**: Agent → Review → Revision → Finalization

### Execution

Delegierbare Repository-Auftraege werden standardmaessig als GPU-Paar ausgefuehrt: genau ein Agent auf NVIDIA und genau ein Agent auf Intel, parallel und mit unabhaengigen Aufgaben oder isolierten Dateien. Der Router verwendet ausschliesslich die im Agent-Frontmatter deklarierte `model`-Bindung und erzeugt oder bindet keine Agents zur Laufzeit um.

Innerhalb jeder GPU bleibt die Ausfuehrung seriell: Vor jedem Modellwechsel wird ueber die LocalAI-API verifiziert, dass der vorherige Worker dieser GPU gestoppt ist. Gemeinsame Modellinstanzen oder zwei Worker auf derselben GPU sind weiterhin unzulaessig. Wenn nur ein GPU-Slot sicher verwendet werden kann, wird keine kuenstliche zweite Aufgabe gestartet.

## Agent Lifecycle

```
Creation → Registration → Task Assignment → Execution → Output → Validation → Completion
```

### State Machine

```
[Idle] → [Task Assigned] → [Executing] → [Output Generated] → [Validated] → [Completed]
    ↑                                      ↑
    └──────────────────────────────────────┘
```

## Routing Strategies

Agent routing determines which agent processes a given request. Strategies include:

- **Rule-based**: Static rules map request types to agents
- **Intent-based**: ML/classification determines agent selection
- **Hybrid**: Rules + intent classification for robust routing
- **Fallback**: Chain of agents with increasing capability

## Configuration

Agent configuration is defined in JSONC format (`agents.jsonc`) and specifies:

- Agent capabilities and domains
- Routing rules and patterns
- Dependencies between agents
- Resource requirements (GPU, memory, timeouts)
- Fallback chains and retry policies

## LocalAI Model Switching

Agents select an existing model-bound agent. LocalAI manages model switching and worker lifetime. Do not manually stop or unload a worker, send shell signals, or restart the LocalAI daemon for a model change. Verify worker state through the LocalAI API before selecting the next model.

## Best Practices

1. **Single Responsibility**: Each agent should have a clear, focused purpose
2. **Explicit Contracts**: Define input/output schemas for all agent interactions
3. **Graceful Degradation**: Implement fallback agents for critical paths
4. **Observability**: Log all agent decisions, routing choices, and outputs
5. **Security**: Enforce least-privilege access for agent tool usage
6. **Idempotency**: Design agent workflows to be safely retryable
7. **Timeouts**: Set reasonable timeouts for all agent operations
8. **Circuit Breakers**: Prevent cascading failures in agent chains

## Integration with MCP

Agents interact with MCP servers through:

- **Tool Execution**: Calling MCP tools for external actions
- **Resource Access**: Reading/writing MCP resources
- **Prompt/Completion**: Generating and consuming prompts
- **Elicitation**: Gathering user context and preferences

## Next Steps

- Review `agents.jsonc` for routing configuration
- Explore `docs/blazor-architecture.md` for Blazor-specific patterns
- Check `docs/projects/LiteLlm.md` for LiteLLM integration status
- Review existing MCP server implementations in `src/Esi.AI.Llm/`
