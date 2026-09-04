# Backend Runtime Gallery

Esi.AI Studio can install verified LLamaSharp native runtime archives through the existing backend requirement page. Configure the gallery under `BackendRuntime` in `appsettings.Development.json`, user secrets, or environment variables.

The gallery catalog is JSON and contains one package per operating-system/runtime combination:

```json
{
  "packages": [
    {
      "id": "llama-sycl-linux-x64-1.0.0",
      "backend": "Llama",
      "route": "sycl",
      "runtimeIdentifier": "linux-x64",
      "version": "1.0.0",
      "archiveUrl": "https://github.com/sidatacom/Esi.AI/releases/download/llama-runtimes-1.0.0/esi-ai-llama-linux-x64-sycl16.zip",
      "sha256": "<64 hexadecimal characters>",
      "requiredFiles": [
        "libllama.so",
        "libggml.so",
        "libggml-base.so",
        "libggml-sycl.so"
      ],
      "driverRequirement": "Intel GPU driver with Level Zero",
      "requiresRestart": true
    }
  ]
}
```

The archive may contain the native files at its root or below one common directory. The installer extracts into a private staging directory, checks SHA-256, rejects unsafe paths, requires exactly one copy of every declared native file, and atomically activates the files under:

```text
runtimes/<runtimeIdentifier>/native/<route>/
```

Supported routes are `cpu`, `cuda12` (also accepted as `cuda`), `sycl` (also accepted as `sycl16`), and `vulkan`. The package backend is currently restricted to `Llama`, because LLamaSharp selects its native backend process-wide. A successful installation therefore reports that Studio must be restarted before loading a model.

For a remote catalog, set:

```json
{
  "BackendRuntime": {
    "CatalogUrl": "https://example.invalid/esi-ai/backend-runtime-catalog.json",
    "Packages": []
  }
}
```

Only remote catalog and archive URLs must use HTTPS. A package is offered by the Solve button only after the catalog entry is available and its runtime route is missing. Build tools such as `icpx` are not end-user installation requirements; the Intel Level Zero loader remains a host prerequisite.

For local development before a release archive exists, set `AllowLocalPackages` to `true` in `appsettings.Development.json` and configure `localPath` on a package instead of using `archiveUrl` and `sha256`. The path is resolved relative to the Studio application directory (`AppContext.BaseDirectory`) and must contain every file listed in `requiredFiles`:

```json
{
  "BackendRuntime": {
    "AllowLocalPackages": true,
    "Packages": [
      {
        "id": "llama-sycl-local-linux-x64",
        "backend": "Llama",
        "route": "sycl",
        "runtimeIdentifier": "linux-x64",
        "version": "0.0.0-local",
        "archiveUrl": "",
        "sha256": "",
        "requiredFiles": [
          "libllama.so",
          "libggml.so",
          "libggml-base.so",
          "libggml-sycl.so",
          "libmtmd.so"
        ],
        "driverRequirement": "Intel GPU driver with Level Zero",
        "requiresRestart": true,
        "localPath": "../../../../../../origins/sidatacom/LLamaSharp/LLama/runtimes/deps/sycl"
      }
    ]
  }
}
```

Build the SYCL libraries into that directory using the commands in the LLamaSharp contributing guide. Local packages are disabled by default, and published packages must use the HTTPS archive and SHA-256 verification flow.

For this fork, the reproducible local command is:

```bash
git -C origins/sidatacom/LLamaSharp submodule update --init --recursive
origins/sidatacom/LLamaSharp/scripts/build-sycl-runtime.sh
```

It builds inside `intel/oneapi-basekit:2025.3.0-0-devel-ubuntu22.04` and stages `libllama.so`, `libggml.so`, `libggml-base.so`, `libggml-sycl.so`, and `libmtmd.so` under `origins/sidatacom/LLamaSharp/LLama/runtimes/deps/sycl`. The host needs Docker; `icpx`, `icx`, CMake, and Ninja are provided by the container. The Intel Level Zero driver remains a runtime prerequisite on the host.