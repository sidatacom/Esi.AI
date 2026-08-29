# Third-party notices

Esi.AI is distributed under the Apache License, Version 2.0. This license applies only to original Esi.AI code and documentation. The components below remain under their own licenses.

## Direct source dependencies

| Component | Location | License | Attribution and source |
| --- | --- | --- | --- |
| LLamaSharp | `origins/sidatacom/LLamaSharp` | MIT | Copyright (c) 2025 SciSharp STACK. The complete license is included in the component's `LICENSE` file. Source: https://github.com/SciSharp/LLamaSharp |
| OpenVINO-CSharp-API | `origins/sidatacom/OpenVINO-CSharp-API` | Apache-2.0 | Copyright and license terms are included in the component's `LICENSE.txt` file. Source: https://github.com/guojin-yan/OpenVINO-CSharp-API |
| dotLLM | `origins/sidatacom/dotLLM` | GPL-3.0 | Copyright and license terms are included in the component's `LICENSE` file. Source: https://github.com/sidatacom/dotLLM |

## Optional external engines

The vLLM and SGLang integrations launch external Python processes and communicate with them through an OpenAI-compatible HTTP API. They are not copied into Esi.AI and remain subject to their respective project licenses and dependency licenses. Their source repositories are:

- vLLM: https://github.com/vllm-project/vllm
- SGLang: https://github.com/sgl-project/sglang

## NuGet packages

The NuGet dependencies declared by the Esi.AI projects retain their respective package licenses and notices. A distribution must include the notices required by those packages. The authoritative package metadata is available from NuGet.org for each package listed in the project files.

## Models and native runtimes

Model files, model weights, CUDA runtimes, OpenVINO runtimes, and other native binaries are not licensed by the Apache-2.0 license for Esi.AI. Each must be obtained and distributed under its own applicable terms.

This file is an attribution record, not a relicensing of third-party software.
