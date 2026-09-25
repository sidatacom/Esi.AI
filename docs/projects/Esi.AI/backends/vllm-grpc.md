# vLLM gRPC bridge

The vLLM backend is started as a local Python process. `Python/inference_server.py`
hosts the gRPC bridge on `127.0.0.1` and loads vLLM through
`AsyncLLMEngine`. The C# process starts the bridge, waits for bridge readiness,
loads the model through `LoadModel`, and streams `Generate` responses.

Every backend goes through the same prerequisite step before model loading.
Native runtimes are bundled with Studio. When either Python backend is loaded
with the default `PythonExecutable` value `python3`, Studio automatically
creates and prepares an isolated environment:

- `~/.venvs/esi-ai-vllm` for vLLM CUDA and `~/.venvs/esi-ai-vllm-xpu` for vLLM XPU
- `~/.venvs/esi-ai-sglang` for SGLang XPU and `~/.venvs/esi-ai-sglang-cuda` for SGLang CUDA

Only missing dependencies are installed. The environment is shared by later
loads of the same backend, but vLLM and SGLang never share an environment.
The equivalent manual installation is:

```bash
python3 -m venv ~/.venvs/esi-ai-vllm
~/.venvs/esi-ai-vllm/bin/python -m pip install -r src/Esi.AI/Esi.AI.Core/Python/vllm-requirements.txt

python3 -m venv ~/.venvs/esi-ai-sglang-cuda
~/.venvs/esi-ai-sglang-cuda/bin/python -m pip install -r src/Esi.AI/Esi.AI.Core/Python/sglang-cuda-requirements.txt
```

The environment root can be changed with `ESI_PYTHON_ENV_ROOT`. Existing
environments can be selected with `ESI_VLLM_PYTHON_EXECUTABLE` or
`ESI_SGLANG_PYTHON_EXECUTABLE`. An explicitly entered executable is validated,
but it is not modified by automatic package installation.

Automatic Python package installation does not install operating-system GPU
drivers or a CUDA toolkit. Those remain machine prerequisites and are reported
by the backend process if they are missing.

The bridge port is the `Port` value in `PythonInferenceLoadRequest`. It binds
only to loopback; it does not expose a public HTTP or gRPC address. The
existing OpenAI-compatible controller and SignalR model-loading path remain
unchanged.

Run the optional integration smoke test with an installed model environment:

```bash
ESI_VLLM_REFERENCE_MODEL=Qwen/Qwen2.5-0.5B-Instruct \
ESI_PYTHON_REFERENCE_EXECUTABLE=python3 \
VLLM_USE_FLASHINFER_SAMPLER=0 \
dotnet test src/Esi.AI/Esi.AI.Core.Tests/Esi.AI.Core.Tests.csproj \
  --filter FullyQualifiedName~LoadReferenceModel_Vllm
```

On this RTX 4070 test environment, vLLM `0.28.0` runs in a Python 3.12
virtual environment with CUDA 13.3, and `Qwen/Qwen2.5-0.5B-Instruct` passed
the end-to-end test. `VLLM_USE_FLASHINFER_SAMPLER=0` avoids a FlashInfer JIT
header mismatch while retaining vLLM's native sampler. The environment also
needs a C/C++ compiler, Ninja, and a CUDA toolkit exposing `nvcc` through
`CUDA_HOME` when vLLM builds runtime kernels.

The fake gRPC transport tests do not require Python, vLLM, a GPU, or a model.

## Structured tools

The bridge preserves OpenAI-compatible `tools` and `tool_choice` as structured
request fields. For vLLM, the tokenizer's chat template receives both values
when it supports them; the bridge does not build a synthetic system prompt.
For SGLang, the same values are forwarded to its OpenAI-compatible HTTP
endpoint. Structured SGLang `message.tool_calls` are returned in
`GenerateResponse.tool_calls_json` and mapped to the shared C# generation
result. The direct vLLM `AsyncLLMEngine` path currently rejects active
structured tool requests because it does not expose the OpenAI server's native
tool parser; it accepts `tool_choice: "none"` as a text-only request instead.

The `system_prompt` is independent model context. It may be included as a
system message, but it is not the tool interface.