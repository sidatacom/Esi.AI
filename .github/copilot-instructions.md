# Copilot Instructions

## Delegate Work to Subagents

Delegate repository work to the most specific custom agent in `.github/agents/` by default. Copilot acts as the coordinator: understand the request, choose one role, provide the relevant context, review the result, and request a focused follow-up when needed. The routing catalogue and fallback metadata live in `.github/agents.jsonc`; VS Code does not execute that JSONC file automatically, so the role agents and the rules below are the executable Copilot integration.

### Routing Table

Select the first matching role. If more than one role matches, use the narrower domain role. For every repository request, resolve both agents through the `agent_model_group_matrix` in `.github/agents.jsonc`; use its exact `agent -> model -> group` mapping and never infer a group from a filename or display name.

| Request intent | Custom agent |
| --- | --- |
| Code review, quality, security audit, style, performance review | `expert-dotnet-software-engineer.agent.md` |
| Implement, generate, refactor, or create code | `expert-csharp.agent.md` |
| Run tests, validate, coverage, or benchmarks | `test-runner.agent.md` |
| Diagnose an error, crash, regression, or slow behavior | `expert-dotnet-software-engineer.agent.md` |
| Documentation, README, API docs, tutorial, or changelog | `model-muse-glimmer-30b.agent.md` |
| Blazor routes, navigation, lifecycle, or component state | `dotnet-fullstack-mentor.agent.md` |
| Blazor architecture, patterns, or best practices | `dotnet-fullstack-mentor.agent.md` |
| MCP server, MCP tool, resource, protocol, or gateway | `csharp-mcp-expert.agent.md` |

Use only the existing agent files and the exact LocalAI model IDs declared in their `model:` frontmatter; never derive a model ID from a display name or filename. Use `model-qwen3.6-27b.agent.md` for general repository work when no role matches. Use `agent-governance-reviewer.agent.md` for agent safety, policy, trust, or audit concerns. If a role is unavailable, use the explicit fallback in `.github/agents.jsonc` and state that fallback in the result.

Use the Qwen model agent for:

- Fast repository exploration and targeted questions
- Summaries, text editing, documentation, and boilerplate
- Standard code, routine refactoring, and straightforward debugging
- Short, focused validation loops

Use the strongest available reasoning agent for:

- Large or cross-module repository context
- Complex debugging and implementation planning
- Mathematics, invariants, parsing, and reasoning-heavy work
- Long documents, broad code review, and precision-critical tool-calling

Delegation rules:

- Do not implement repository changes directly when a matching custom agent can perform the work.
- Give both selected subagents a concrete task, relevant paths, constraints, and expected validation.
- For every repository request that can be delegated, dispatch exactly two independent subagents in parallel: one eligible NVIDIA agent and one eligible Intel agent. The selected role agent occupies the group declared by the matrix; dispatch exactly one model-bound agent for the opposite group. Do not add a third role or coordinator agent, and do not wait for the first subagent before starting the second.
- The NVIDIA and Intel subagents must receive independent scopes or read-only analysis tasks. They must not edit the same files concurrently; the coordinator merges or applies follow-up changes after both results are reviewed.
- If the configured pair cannot be resolved, fail closed with a routing error. Never silently start only one subagent or substitute an unlisted model.
- Each model is permanently assigned to exactly one GPU and one backend. Never move, load, or execute a model on another GPU, backend, worker, or LocalAI API instance.
- Never run two agents on the same GPU concurrently. LocalAI manages model switching and worker lifetime; before selecting another model on a GPU, verify through the LocalAI API that the previous worker is stopped.
- Review the subagent's findings and diff before reporting completion.
- Run or request focused validation after every substantive change.
- If a subagent cannot complete the task, clarify the missing context and delegate a focused follow-up to the same or more suitable subagent.
- Handle only coordination, user clarification, final review, and work that cannot be delegated.
- Respect repository instructions and never undo unrelated user changes.

## Delegation Rules

- Select the first matching role and use the exact `model` in that agent's frontmatter. Do not infer a model from a role name or display label.
- Route by the agent's declared `model` binding. The router must never infer a GPU from an agent's role name and must never create or rebind an agent at runtime.
- Each model is permanently assigned to exactly one GPU and one backend. Never move, load, or execute a model on another GPU, backend, worker, or LocalAI API instance.
- Do not collapse a configured pair because the work appears small. If the two scopes cannot be isolated, assign the second subagent a read-only architecture, test, or governance analysis.
- Do not select another model until the LocalAI API confirms that the previous worker is stopped. Do not create or rebind agents at runtime.
- Parallel delegation is mandatory for independent repository work: always use one NVIDIA and one Intel subagent when both GPU slots are available.

### Model/GPU Matrix

The existing model-bound agents use the following fixed GPU assignments:

| GPU | Models | Agent files |
| --- | --- | --- |
| NVIDIA | `gemma-4-12b-it-qat-mtp`, `qwen3.6-14b` | `model-gemma-4.agent.md`, `model-qwen3.6-14b.agent.md` |
| Intel | `qwen3.6-27b`, `qwen3.8-27b-q4`, `nemotron-3.5-lightning-30b-a3b-nvfp4`, `glm-4.7-flash` | `model-qwen3.6-27b.agent.md`, `model-qwen3.8-27b-q4.agent.md`, `model-muse-glimmer-30b.agent.md`, `model-glm-4.7-flash.agent.md` |

Scheduling must preserve these invariants:

- Both GPU slots must be utilized for every delegable repository request whenever eligible agents are available.
- At most two subagents may run concurrently: one on NVIDIA and one on Intel.
- At most one model worker may be active on each GPU at any time.
- Models assigned to the same GPU must never be started concurrently.
- Before switching to another model on a GPU, verify through the LocalAI API that the previous worker on that GPU has stopped.
- Do not use shell signals or restart the LocalAI daemon to stop model workers.
- A request is not complete until both configured pair members have been started and their results reviewed.

## Namespace Rules

- `Esi.AI.Llm` ist der einzige erlaubte Root-Namespace für alle Objekte in diesem Projekt. Neue Typen (Klassen, Interfaces, Models, Provider, Services, etc.) sind unter `Esi.AI.Llm` anzulegen; Separate Root-Namen wie `Esi.AI.LiteLlm` sowie beliebige andere Root-Namen sind unzulässig. LiteLLM-abhängige Funktionalität wird ausschliesslich unter `Esi.AI.Llm` integriert, nicht unter separaten LiteLLM-Namenpaces.
