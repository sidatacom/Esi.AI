# Esi.AI

Esi.AI is a local-first C# AI workspace for VS Code and GitHub Copilot. It combines Esi.AI Studio, native model runtimes, OpenAI-compatible APIs, SignalR application services, and MCP tooling in one development environment.

Esi.AI is licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE). Third-party components retain their original licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## What is included

- **Esi.AI Studio**: .NET 10 Blazor Web App with Interactive Auto, GPU/backend discovery, model loading, device routing, OpenAI-compatible endpoints, OpenAPI, Swagger, and SignalR.
- **VS Code integration**: The Esi.AI Studio language-model provider and the DebugMCP/Terminal MCP tooling live under [`src/vscode`](src/vscode).
- **Native and Python backends**: LLamaSharp/GGUF, OpenVINO, dotLLM, vLLM, and SGLang are available through the Studio backend surface.
- **Local orchestration**: LiteLLM and LocalAI sources are kept under [`origins`](origins) for local provider and inference workflows.

## Esi.AI Studio

The Studio backend page brings together model configuration, GPU routing, backend compatibility, and runtime diagnostics. The current setup exposes CUDA, Vulkan, SYCL, OpenVINO, vLLM, SGLang, and dotLLM routes where the required drivers and runtimes are available.

<p align="center">
	<img src="docs/images/esi-ai-studio-backends.png" alt="Current Esi.AI Studio backend overview with runtime status, model catalog, and device routing" width="960" />
</p>
<p align="center"><em>Current backend overview, runtime status, model catalog, and device routing.</em></p>

<p align="center">
	<img src="docs/images/esi-ai-studio-sycl-config.png" alt="Current Esi.AI Studio OpenVINO device selection and model loading configuration" width="960" />
</p>
<p align="center"><em>Current OpenVINO device selection and model loading configuration.</em></p>

## Documentation

- [Reference models and native load/generate checks](docs/reference-models.md)
- [Backend runtime gallery](docs/backend-runtime-gallery.md)
- [OpenVINO GenAI and GGUF notes](docs/openvino-genai-gguf.md)
- [vLLM gRPC and Python runtime setup](docs/vllm-grpc.md)
- [Esi.AI Studio debugging and Hot Reload](docs/studio-development.md)
- [Third-party notices and license boundaries](THIRD-PARTY-NOTICES.md)

## Repository structure

- `src/Esi.AI/`: Esi.AI Studio, shared models and contracts, native runtime integrations, and tests.
- `src/vscode/vscode-esi-ai-studio/`: VS Code language-model provider for Esi.AI Studio.
- `src/vscode/vscode-esi-mcp/`: VS Code extension and MCP server components for debugging, terminal control, and the Microsoft Access wrapper.
- `origins/litellm/`: LiteLLM source submodule.
- `origins/localai/`: LocalAI source submodule.
- `origins/brickly26/MS-Access-mcp/`: Microsoft Access MCP server source submodule wrapped by EsiMCP.
- `origins/sidatacom/`: Local forks of LLamaSharp, OpenVINO-CSharp-API, dotLLM, and Fluent UI Blazor.

## Referenced projects and greetings

A warm hello and sincere thanks to every project that makes this workspace possible. Esi.AI keeps the local source relationship, fork relationship, or external runtime boundary explicit for each reference.

| Project | How Esi.AI uses it | Local or upstream reference |
| --- | --- | --- |
| [sidatacom/Esi.AI](https://github.com/sidatacom/Esi.AI) | Upstream project and public home of this workspace. | [GitHub repository](https://github.com/sidatacom/Esi.AI) |
| [Microsoft Fluent UI Blazor](https://github.com/microsoft/fluentui-blazor) | Blazor UI components and charts used by Esi.AI Studio. | Local fork: [`origins/sidatacom/fluentui-blazor`](origins/sidatacom/fluentui-blazor) |
| [SciSharp/LLamaSharp](https://github.com/SciSharp/LLamaSharp) | .NET access to llama.cpp-compatible GGUF inference. | Local fork: [`origins/sidatacom/LLamaSharp`](origins/sidatacom/LLamaSharp) |
| [ggml-org/llama.cpp](https://github.com/ggml-org/llama.cpp) | Underlying native ecosystem and GGUF runtime lineage for the LLamaSharp backend. | External upstream project |
| [guojin-yan/OpenVINO-CSharp-API](https://github.com/guojin-yan/OpenVINO-CSharp-API) | C# OpenVINO integration used by the native OpenVINO backend. | Local fork: [`origins/sidatacom/OpenVINO-CSharp-API`](origins/sidatacom/OpenVINO-CSharp-API) |
| [kkokosa/dotLLM](https://github.com/kkokosa/dotLLM) | .NET GGUF runtime path for the dotLLM backend. | Local fork: [`origins/sidatacom/dotLLM`](origins/sidatacom/dotLLM) |
| [BerriAI/litellm](https://github.com/BerriAI/litellm) | Embedded provider routing and unified model API source. | [`origins/litellm`](origins/litellm) |
| [mudler/LocalAI](https://github.com/mudler/LocalAI) | Embedded local inference and provider integration source. | [`origins/localai`](origins/localai) |
| [vLLM](https://github.com/vllm-project/vllm) | External Python inference engine reached through the local bridge. | External runtime boundary |
| [SGLang](https://github.com/sgl-project/sglang) | External Python inference engine reached through the local bridge. | External runtime boundary |

The original licenses and attribution requirements remain with these projects and their dependencies. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) before redistributing a build.

## Development

Build the Studio project with:

```bash
dotnet build src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj
```

Start Esi.AI Studio with C# Dev Kit using **Start New Instance** on the server project. C# Dev Kit uses a dynamic in-memory debug configuration, so the normal workflow does not require `.vscode/launch.json` or `.vscode/tasks.json`. The standard Microsoft Blazor lifecycle is used for server debugging, WebAssembly debugging, and Hot Reload; no custom watchdog is required.

