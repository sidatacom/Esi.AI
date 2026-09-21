# Backend reference models

These models are intentionally small enough for smoke and integration tests. The model files are not committed to the repository.

## Active browser-test reference family

The active browser test plan uses one logical Qwen2.5 Instruct reference family. A single physical file cannot serve every runtime: LLama requires GGUF, OpenVINO requires OpenVINO IR, and vLLM requires Transformers/Safetensors. Pin the same model revision where a backend conversion supports it and record the conversion revision for each artifact.

| Backend variant | Required format | Test setting |
| --- | --- | --- |
| LLama Vulkan, CUDA 12, SYCL 16 | Qwen2.5 Instruct GGUF | `ESI_LLAMA_MODEL_PATH` |
| OpenVINO configured device | Qwen2.5 Instruct OpenVINO IR | `ESI_OPENVINO_MODEL_PATH` |
| vLLM NVIDIA / CUDA, Intel / XPU | Qwen2.5 Instruct Transformers/Safetensors | `ESI_VLLM_REFERENCE_MODEL` |

The existing backend-specific entries below are legacy/native reference-test defaults. They must not be reported as the common browser-test reference family until the Qwen2.5 artifacts above are available and pinned. SGLang and dotLLM remain deferred in the browser plan.

| Backend | Reference model | Format | Test setting |
| --- | --- | --- | --- |
| LLama | [SmolLM2-135M-Instruct-GGUF](https://huggingface.co/bartowski/SmolLM2-135M-Instruct-GGUF), `SmolLM2-135M-Instruct-Q4_K_M.gguf` | GGUF | `ESI_LLAMA_MODEL_PATH` |
| OpenVINO | [OpenVINO/Qwen2.5-1.5B-Instruct-int4-ov](https://huggingface.co/OpenVINO/Qwen2.5-1.5B-Instruct-int4-ov) | OpenVINO IR directory | `ESI_OPENVINO_MODEL_PATH` |
| vLLM | [Qwen/Qwen2.5-0.5B-Instruct](https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct) | Transformers / Safetensors | `ESI_VLLM_REFERENCE_MODEL` |
| SGLang | [Qwen/Qwen2.5-0.5B-Instruct](https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct) | Transformers / Safetensors | `ESI_SGLANG_REFERENCE_MODEL` |
| dotLLM | [SmolLM2-135M-Instruct-GGUF](https://huggingface.co/bartowski/SmolLM2-135M-Instruct-GGUF), `SmolLM2-135M-Instruct-Q4_K_M.gguf` | GGUF | `ESI_DOTLLM_MODEL_PATH` |

## Download

```bash
mkdir -p "$HOME/.cache/esi-ai/reference-models"

huggingface-cli download bartowski/SmolLM2-135M-Instruct-GGUF \
  --include 'SmolLM2-135M-Instruct-Q4_K_M.gguf' \
  --local-dir "$HOME/.cache/esi-ai/reference-models/smollm2"

huggingface-cli download OpenVINO/Qwen2.5-1.5B-Instruct-int4-ov \
  --local-dir "$HOME/.cache/esi-ai/reference-models/qwen2.5-openvino"
```

The Python backends download `Qwen/Qwen2.5-0.5B-Instruct` from Hugging Face when their inference server starts. Install the selected engine in the Python environment used by Studio:

```bash
python3 -m pip install vllm
python3 -m pip install sglang[all]
```

## Native reference tests

Configure the local paths and run all five backend reference tests:

```bash
export ESI_LLAMA_MODEL_PATH="$HOME/.cache/esi-ai/reference-models/smollm2/SmolLM2-135M-Instruct-Q4_K_M.gguf"
export ESI_DOTLLM_MODEL_PATH="$ESI_LLAMA_MODEL_PATH"
export ESI_OPENVINO_MODEL_PATH="$HOME/.cache/esi-ai/reference-models/qwen2.5-openvino"
export ESI_OPENVINO_DEVICE="GPU.0"
export ESI_VLLM_REFERENCE_MODEL="Qwen/Qwen2.5-0.5B-Instruct"
export ESI_SGLANG_REFERENCE_MODEL="Qwen/Qwen2.5-0.5B-Instruct"

dotnet test src/Esi.AI/Esi.AI.Core.Tests/Esi.AI.Core.Tests.csproj \
  --filter 'TestCategory=BackendReference'
```

The LLama, OpenVINO, and dotLLM tests require the local model files and compatible native devices. The vLLM and SGLang tests require a working Python installation with the corresponding engine. When these variables are unset, the tests are reported as inconclusive instead of failing the normal test suite.
