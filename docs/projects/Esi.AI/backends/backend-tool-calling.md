# Structured tool calling

Esi.AI keeps `tools` and `tool_choice` as structured OpenAI-compatible request
data until the selected runtime adapter. A backend is responsible for applying
its model-native chat template and for parsing the model output back into
structured tool calls. Esi.AI does not inject a universal `# Tools` system
prompt.

| Backend | Tool definitions | Tool-choice handling | Output handling |
| --- | --- | --- | --- |
| OpenVINO | `ChatHistory.SetTools` | `none` removes the native tool context; a specific function narrows the tool list | OpenVINO response is parsed into OpenAI tool calls |
| vLLM | Hugging Face tokenizer template receives `tools` and `tool_choice` where supported | The direct `AsyncLLMEngine` path rejects active structured tools; `none` remains text-only | No silent text-to-tool conversion; use the SGLang/OpenAI-compatible path or another backend with a native parser |
| SGLang | OpenAI-compatible backend receives structured `tools` and `tool_choice` | Passed unchanged to SGLang | `message.tool_calls` is returned over the Python gRPC contract |
| dotLLM | dotLLM Jinja chat template receives native `ToolDefinition` values | `required` and specific functions use dotLLM constrained JSON schemas; `none` removes tools | dotLLM model-specific parser returns structured calls |
| LLamaSharp | Not available in the current LLamaSharp chat API | Structured tool requests are rejected explicitly | No silent prompt flattening |

The Python bridge mirrors the contract in
`src/Esi.AI/Esi.AI.Models/Grpc/vllm_inference.proto`. Tool definitions are
carried in `GenerateRequest.tools`, the raw `tool_choice` value in
`GenerateRequest.tool_choice_json`, and parsed calls in
`GenerateResponse.tool_calls_json`.

## Request lifecycle

1. The OpenAI-compatible controller validates and preserves the incoming
   messages, images, tools, and `tool_choice`.
2. The selected adapter maps those values to the runtime's native interface.
3. The runtime applies the model's tokenizer/Jinja template, or its native
   equivalent such as OpenVINO GenAI or dotLLM.
4. The runtime parser returns structured calls. The controller emits them as
   `assistant.tool_calls` with `finish_reason: "tool_calls"`.

The model YAML `system_prompt` remains a normal system message. It may provide
instructions, but it is not the tool transport and must not be used as a
backend-independent replacement for `tools`.