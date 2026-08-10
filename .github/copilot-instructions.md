# Copilot Instructions

## Delegate Work to Subagents

Delegate all repository work to a custom subagent by default. Copilot should act as the coordinator: understand the request, choose the appropriate subagent, provide it with the relevant context, review its result, and request a focused follow-up when needed.

Use the `qwen` subagent for:

- Fast repository exploration and targeted questions
- Summaries, text editing, documentation, and boilerplate
- Standard code, routine refactoring, and straightforward debugging
- Short, focused validation loops

Use the `bonsai` subagent for:

- Large or cross-module repository context
- Complex debugging and implementation planning
- Mathematics, invariants, parsing, and reasoning-heavy work
- Long documents, broad code review, and precision-critical tool-calling

Delegation rules:

- Do not implement repository changes directly when a subagent can perform the work.
- Give the selected subagent a concrete task, relevant paths, constraints, and expected validation.
- Delegate one model task at a time. Never load or test both local models in parallel.
- Review the subagent's findings and diff before reporting completion.
- Run or request focused validation after every substantive change.
- If a subagent cannot complete the task, clarify the missing context and delegate a focused follow-up to the same or more suitable subagent.
- Handle only coordination, user clarification, final review, and work that cannot be delegated.
- Respect repository instructions and never undo unrelated user changes.

## Parallel Delegation Rules

- Delegate tasks for independent operations in parallel using isolated resources. Qwen and Bonsai can run concurrently on separate GPUs and isolated LocalAI worker/API instances.
- Prohibit parallel delegation when: shared files, dependent steps, or same model instance.
- Verify GPU and LocalAI isolation before starting parallel tasks.

## Namespace Rules

- `Esi.AI.Llm` ist der einzige erlaubte Root-Namespace für alle Objekte in diesem Projekt. Neue Typen (Klassen, Interfaces, Models, Provider, Services, etc.) sind unter `Esi.AI.Llm` anzulegen; Separate Root-Namen wie `Esi.AI.LiteLlm` sowie beliebige andere Root-Namen sind unzulässig. LiteLLM-abhängige Funktionalität wird ausschliesslich unter `Esi.AI.Llm` integriert, nicht unter separaten LiteLLM-Namenpaces.
