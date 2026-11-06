import json
from typing import Any


def parse_tool_calls_for_chat_template(tool_calls_json: str) -> list[dict[str, Any]]:
    tool_calls = json.loads(tool_calls_json)
    if not isinstance(tool_calls, list):
        raise ValueError("Tool calls JSON must contain an array.")

    for index, tool_call in enumerate(tool_calls):
        if not isinstance(tool_call, dict):
            raise ValueError(f"Tool call at index {index} must be an object.")

        function = tool_call.get("function")
        if not isinstance(function, dict):
            raise ValueError(f"Tool call at index {index} must contain a function object.")

        arguments = function.get("arguments")
        if isinstance(arguments, str):
            try:
                arguments = json.loads(arguments)
            except json.JSONDecodeError as exception:
                raise ValueError(f"Tool call at index {index} has invalid JSON arguments.") from exception

        if not isinstance(arguments, dict):
            raise ValueError(f"Tool call at index {index} arguments must be a JSON object.")

        function["arguments"] = arguments

    return tool_calls
