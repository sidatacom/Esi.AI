from typing import Any


_REASONING_EFFORTS = {"none", "low", "medium", "high", "xhigh", "max"}


def add_reasoning_effort_template_options(
    template_options: dict[str, Any],
    tokenizer: Any,
    reasoning_effort: str | None,
) -> None:
    if reasoning_effort is None or not reasoning_effort.strip():
        return

    normalized_effort = reasoning_effort.strip().lower()
    if normalized_effort not in _REASONING_EFFORTS:
        raise ValueError(f"Unsupported reasoning effort {reasoning_effort!r}.")

    chat_template = getattr(tokenizer, "chat_template", None)
    if _template_uses_variable(chat_template, "enable_thinking"):
        template_options["enable_thinking"] = normalized_effort != "none"
    if _template_uses_variable(chat_template, "reasoning_effort"):
        template_options["reasoning_effort"] = (
            "xhigh" if normalized_effort in {"high", "max"} else normalized_effort
        )


def _template_uses_variable(template: Any, variable: str) -> bool:
    if isinstance(template, str):
        return variable in template
    if isinstance(template, dict):
        return any(_template_uses_variable(value, variable) for value in template.values())
    if isinstance(template, list):
        return any(_template_uses_variable(value, variable) for value in template)
    return False
