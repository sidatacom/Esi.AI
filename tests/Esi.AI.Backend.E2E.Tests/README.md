# Backend Chat End-to-End Tests

This opt-in suite sends a real chat for each packaged backend variant through both `POST /v1/chat/completions` and the Studio `/chats` browser page. Each case uses the family's configured reference model and checks that the browser response is attributed to the expected runtime.

The suite never downloads model artifacts. It discovers the canonical reference model in the Studio model catalog, clones the saved backend-specific settings into a temporary configuration, loads the selected variant, and unloads it and deletes the temporary configuration after the case. A saved configuration for each target variant must exist in Studio; its backend-specific options are the load template.

The six cases are LLama Vulkan, CUDA 12, and SYCL; OpenVINO; and vLLM CUDA 12 and XPU. Set `ESI_BACKEND_CHAT_E2E=1` to opt into real inference. Studio defaults to `http://127.0.0.1:7010`; override it with `ESI_STUDIO_BASE_URL` if needed. The optional model-family variables `ESI_LLAMA_MODEL_PATH`, `ESI_OPENVINO_MODEL_PATH`, and `ESI_VLLM_REFERENCE_MODEL` select a model path from the Studio catalog. When omitted, the test discovers the matching local reference model by its canonical model ID and format. The Studio URL must be loopback.

After building once, install the Playwright Chromium runtime if needed:

```bash
pwsh tests/Esi.AI.Backend.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Run the suite directly so the existing solution's unrelated `origins/**` projects are not built:

```bash
dotnet test tests/Esi.AI.Backend.E2E.Tests/Esi.AI.Backend.E2E.Tests.csproj --filter "TestCategory=EndToEnd"
```