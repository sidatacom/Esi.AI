---
name: Esi.AI Studio WebAPI
description: "Guidance for the Esi.AI Studio OpenAI-compatible WebAPI, application model lifecycle routes, multimodal request parsing, backend capability checks, and API tests."
applyTo: 'src/Esi.AI/Esi.AI.Studio/Controllers/OpenAiCompatibleController.cs'
---

# Esi.AI Studio WebAPI

## Ownership and boundaries

- The application exposes exactly one HTTP controller: `OpenAiCompatibleController`.
- Keep OpenAI-compatible traffic under `/v1`. Do not add a second controller, Minimal API endpoint, or ad hoc HTTP endpoint for application behavior.
- `/v1/models` and `/v1/chat/completions` are the OpenAI-compatible contract.
- Model lifecycle operations are application operations under `/v1/application/models` and are intended for local administration, not for OpenAI client compatibility.
- Browser application operations continue to use `IDataService`, `DataService`, and `DataHub` at `/hubs/data`. Do not replace SignalR browser flows with direct HTTP calls.
- `OpenAiCompatibleController` delegates model loading and unloading to `DataService`; it must not contain loader or inference business logic.
- All request, response, status, and SignalR contract types belong in `Esi.AI.Models`.

## Application model lifecycle API

Current routes:

```text
GET  /v1/application/models
GET  /v1/application/models/catalog
POST /v1/application/models/load
POST /v1/application/models/load/llama
POST /v1/application/models/load/openvino
POST /v1/application/models/load/python
POST /v1/application/models/load/dotllm
POST /v1/application/models/unload
```

Use the existing typed contracts:

- `LoadModelRequest` for LLamaSharp. It includes the LLama backend string, context size, Vulkan device weights, advanced settings, and optional `MmprojPath`.
- `OpenVinoLoadRequest` for OpenVINO. Use a GPU, `MULTI:GPU`, or NPU device according to the loader rules.
- `PythonInferenceLoadRequest` for vLLM or SGLang. The `Backend` must be `Vllm` or `Sglang`.
- `DotLlmLoadRequest` for the in-process dotLLM runtime.
- `ApplicationModelUnloadRequest` for targeted unload. Send a model path and a `ConfigurationBackend` string such as `Llama`, `OpenVino`, `Vllm`, `Sglang`, or `DotLlm`.
- `ApplicationModelCatalog` is the discovery contract for application loading. It synchronizes the internal `Model` records and returns their stable `Model.Id` values alongside persisted `ModelConfiguration` records.
- `ApplicationModelLoadRequest` selects one internal `ModelId` and one matching `ConfigurationId`; the server resolves the stored JSON into the existing typed backend load request and overwrites only the model path from the internal model record.

The status response is the existing `ModelLoadStatus` snapshot. It contains the current primary runtime and the `LoadedModels` collection, including pending model loads.

Recommended application load flow:

```http
GET  http://127.0.0.1:7010/v1/application/models/catalog
POST http://127.0.0.1:7010/v1/application/models/load
Content-Type: application/json
```

```json
{
  "modelId": "00000000-0000-0000-0000-000000000000",
  "configurationId": "00000000-0000-0000-0000-000000000000"
}
```

The two IDs must come from the catalog and the selected configuration must belong to the selected model. The older backend-specific routes remain compatibility routes and accept their existing typed payloads.

Example:

```http
GET http://127.0.0.1:7010/v1/application/models
```

```json
{
  "modelPath": "/models/model.gguf",
  "backend": "CPU",
  "isModelLoaded": true,
  "loadedModels": []
}
```

## Chat request rules

- `OpenAiChatMessage.Content` may be a string, a JSON string value, or an array of OpenAI content parts.
- Local multimodal content supports only `text` and `image_url` parts.
- Images must use a local Base64 data URL with an `image/*` media type, for example `data:image/png;base64,...`.
- Remote URLs are rejected and must never be downloaded by the local server.
- Reject malformed Base64, missing `image_url.url`, non-image media types, empty image data, and images larger than 20 MiB.
- Parse the request once into transport-neutral `ChatMessage`, `ChatImage`, and `ChatMessageContentPart` values.
- Preserve the original structured OpenAI messages for OpenVINO history/template rendering. The normalized text projection is for backends that need plain text only.
- Validate image capability after resolving the selected loaded model. Do not use a stored capability flag as a substitute for the runtime check.

## Backend capability matrix

| Backend | Local image request | Rule |
|---|---:|---|
| OpenVINO VLM | Supported when loaded as a VLM | Requires the vision model directory and a runtime compatible with the model. |
| LLamaSharp CPU/Vulkan | Supported when MTMD vision is loaded | Requires a compatible `mmproj` sidecar and `MtmdWeights.SupportsVision`. |
| vLLM | Not supported by this local bridge | Keep the current text-only Python/gRPC contract unchanged. |
| SGLang | Not supported by this local bridge | Keep the current text-only Python/gRPC contract unchanged. |
| dotLLM | Not supported | The in-process integration is text-only. |

A backend that cannot process images must return `unsupported_request_error`; it must not silently discard image parts or claim `imageInput` capability.

## Security and error handling

- Mutating application model routes are loopback-only. Reject non-loopback callers before invoking `DataService`.
- Treat model paths and load options as untrusted input. Let the existing loaders enforce file, extension, device, and option validation.
- Map malformed requests to `400` with the existing `OpenAiErrorResponse` shape.
- Map unavailable or failed local runtime operations to `503` where the existing controller convention requires it.
- Observe the request cancellation token. Do not create a second unmanaged model-loading path.
- Do not expose secrets, environment values, or full remote stack traces in a public API response.

## Testing requirements

Use test names in the form `MethodName_Condition_ExpectedResult()` and cover:

- application status when no model is loaded;
- missing and invalid backend-specific load bodies;
- targeted unload with a string backend value;
- non-loopback mutation rejection;
- local Base64 image parsing and remote URL rejection;
- image capability rejection for text-only backends;
- successful semantic image requests for each backend that actually supports them.

Do not start the chat image picker implementation until the OpenVINO and LLamaSharp WebAPI paths have passed their real image tests.
