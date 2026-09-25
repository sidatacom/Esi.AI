# Backend Assemblies

## Target Packages

Each installable backend variant is a separate assembly and NuGet package:

- `Esi.AI.Backend.Llama.Vulkan`
- `Esi.AI.Backend.Llama.Cuda12`
- `Esi.AI.Backend.Llama.Sycl`
- `Esi.AI.Backend.OpenVino`
- `Esi.AI.Backend.Vllm.Cuda12`
- `Esi.AI.Backend.Vllm.Xpu`

All six packages are implemented as standalone runtime projects and are referenced and registered by Studio.

All packages implement `Esi.AI.Backend.Abstractions`. The abstraction owns runtime lifecycle, capability reporting, model status, and normalized chat generation. `Esi.AI.Models` owns transport-neutral requests and results. A backend package owns its engine integration, route-specific configuration, native/Python runtime assets, and package dependencies. The Studio host discovers/registers installed modules and coordinates them; it must not reference engine-specific runtime types.

There is one backend-facing runtime interface, `IBackendRuntime`, for load/unload/status/capability and normalized chat generation. Chat and OpenAI-compatible WebAPI callers should converge on that same interface; backend packages must not introduce separate chat adapters and WebAPI adapters.

```text
Esi.AI.Studio
  -> Esi.AI.Core (orchestration only)
  -> Esi.AI.Backend.Abstractions
  -> selected Esi.AI.Backend.* packages

Esi.AI.Backend.*
  -> Esi.AI.Backend.Abstractions
  -> Esi.AI.Models
  -> its inference engine and route-specific assets
```

Backend family (`Llama`, `Vllm`) and variant (`llama.vulkan`, `vllm.xpu`) are separate identifiers. A variant owns a descriptor and load configuration, while the shared family name remains available for persisted model compatibility.

## Current Migration State

The package-facing contract and module registration extension are implemented in `Esi.AI.Backend.Abstractions`, and the shared `GenerationResult` now lives in `Esi.AI.Models`. The abstractions package builds and packs without an inference-engine dependency.

Standalone runtime projects exist for LLama Vulkan/CUDA 12/SYCL, OpenVINO, and vLLM CUDA 12/XPU. Each provides an `IBackendRuntime` implementation and backend module. They depend on the backend abstractions and shared models, not on `Esi.AI.Core`.

Studio registers all six packaged modules through `AddEsiAiBackendModules`. Persisted model profiles carry a stable `BackendVariantId`, `ModelRuntime` resolves packaged load/status/generation/unload/stop operations by that ID, and SignalR chat plus the OpenAI-compatible WebAPI both invoke the normalized `IBackendRuntime.GenerateAsync` contract. Core-owned CPU LLama, SGLang, and dotLLM continue using legacy adapters because no standalone modules for those variants exist yet. The renamed worker is `Esi.AI.Backend.Worker`; it remains a prerequisite/diagnostic process rather than a long-lived inference host.

The remaining extraction work is to add standalone packages for LLama CPU, SGLang, and dotLLM, then remove their corresponding engine/server references and legacy adapters from Core. Studio/Core still retain compatibility references to engine libraries for these remaining implementations. Each removal needs focused load, capability, unload, generation, and shutdown coverage.