"""Apply Esi.AI runtime compatibility hooks before spawned inference workers import vLLM."""

import os


if os.environ.get("VLLM_TARGET_DEVICE", "").lower() == "xpu":
    import vllm_xpu_bootstrap

    vllm_xpu_bootstrap.disable_cuda_platform_probe()
    vllm_xpu_bootstrap.enable_xpu_memory_probe_fallback()
    vllm_xpu_bootstrap.install_vllm_memory_probe_hook()
