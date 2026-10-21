"""Compatibility hooks for vLLM XPU environments on mixed-vendor hosts."""

from functools import wraps
from types import ModuleType
import argparse
import importlib
import importlib.abc
import importlib.machinery
import json
import os
from pathlib import Path
import sys


def disable_cuda_platform_probe() -> None:
    """Prevent vLLM from activating CUDA based on host-wide NVML data."""
    module_name = "vllm.third_party.pynvml"
    if module_name in sys.modules:
        return
    pynvml = ModuleType(module_name)
    pynvml.nvmlInit = lambda: None
    pynvml.nvmlDeviceGetCount = lambda: 0
    pynvml.nvmlShutdown = lambda: None
    sys.modules[module_name] = pynvml


def enable_xpu_memory_probe_fallback() -> None:
    """Use XPU total memory when Torch cannot report available device memory."""
    try:
        import torch
    except ImportError:
        return
    accelerator = torch.accelerator
    if getattr(accelerator.get_memory_info, "_esi_xpu_fallback", False):
        return
    original_get_memory_info = accelerator.get_memory_info

    @wraps(original_get_memory_info)
    def get_memory_info(device=None):
        requested_device = device
        if requested_device is None:
            requested_device = torch.device("xpu", torch.xpu.current_device())
        requested_device = torch.device(requested_device)
        try:
            free_memory, total_memory = original_get_memory_info(device)
        except RuntimeError as exception:
            if requested_device.type != "xpu" or "doesn't support querying" not in str(exception):
                raise
            total_memory = torch.xpu.get_device_properties(requested_device.index).total_memory
            reserved_memory = torch.accelerator.memory_reserved(requested_device)
            return max(total_memory - reserved_memory, 0), total_memory
        if requested_device.type != "xpu" or total_memory <= 0 or free_memory > 0:
            return free_memory, total_memory
        total_memory = torch.xpu.get_device_properties(requested_device.index).total_memory
        reserved_memory = torch.accelerator.memory_reserved(requested_device)
        return max(total_memory - reserved_memory, 0), total_memory

    get_memory_info._esi_xpu_fallback = True
    accelerator.get_memory_info = get_memory_info


def install_vllm_memory_probe_hook() -> None:
    """Reapply the XPU memory fallback after vLLM initializes Torch."""
    if any(isinstance(finder, _VllmImportFinder) for finder in sys.meta_path):
        return
    sys.meta_path.insert(0, _VllmImportFinder())


class _VllmImportFinder(importlib.abc.MetaPathFinder):
    def find_spec(self, fullname, path, target=None):
        if fullname != "vllm":
            return None
        sys.meta_path.remove(self)
        try:
            spec = importlib.machinery.PathFinder.find_spec(fullname, path)
        finally:
            sys.meta_path.insert(0, self)
        if spec is not None and spec.loader is not None:
            spec.loader = _VllmImportLoader(spec.loader)
        return spec


class _VllmImportLoader(importlib.abc.Loader):
    def __init__(self, loader):
        self.loader = loader

    def create_module(self, spec):
        create_module = getattr(self.loader, "create_module", None)
        return create_module(spec) if create_module is not None else None

    def exec_module(self, module):
        self.loader.exec_module(module)
        enable_xpu_memory_probe_fallback()


def _find_level_zero_loader() -> Path | None:
    for candidate in (
        Path("/usr/lib/x86_64-linux-gnu/libze_loader.so"),
        Path("/usr/lib/x86_64-linux-gnu/libze_loader.so.1"),
        Path("/usr/lib64/libze_loader.so"),
        Path("/usr/lib64/libze_loader.so.1"),
    ):
        if candidate.is_file():
            return candidate
    return None


def _join_unique_paths(*paths: str) -> str:
    unique_paths = dict.fromkeys(path for path in paths if path)
    return os.pathsep.join(unique_paths)


def _get_setting(settings: dict[str, object], key: str, default: str) -> str:
    value = settings.get(key, default)
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"The vLLM XPU setting '{key}' must be a non-empty string.")
    return value


def build_xpu_process_environment(
    inherited_environment: dict[str, str],
    python_executable: str,
    xpu_settings: dict[str, object],
    enable_xpu_graph: bool,
    enable_bf16_mtp_draft: bool,
) -> dict[str, str]:
    """Build the process environment from the selected vLLM XPU profile."""
    environment = inherited_environment.copy()
    python_directory = Path(__file__).resolve().parent
    environment["PYTHONPATH"] = _join_unique_paths(
        str(python_directory),
        environment.get("PYTHONPATH", ""),
    )
    environment["CUDA_VISIBLE_DEVICES"] = ""
    environment["ONEAPI_DEVICE_SELECTOR"] = _get_setting(xpu_settings, "oneApiDeviceSelector", "level_zero:gpu")
    environment["ZE_FLAT_DEVICE_HIERARCHY"] = _get_setting(xpu_settings, "zeFlatDeviceHierarchy", "COMPOSITE")
    environment["PYTORCH_ALLOC_CONF"] = _get_setting(xpu_settings, "pyTorchAllocatorConfig", "expandable_segments:True")
    environment["VLLM_XPU_ENABLE_XPU_GRAPH"] = "1" if enable_xpu_graph else "0"
    environment["B70_MTP_BF16_DRAFT"] = "1" if enable_bf16_mtp_draft else "0"
    environment["VLLM_TARGET_DEVICE"] = "xpu"
    environment["CCL_ZE_IPC_EXCHANGE"] = _get_setting(xpu_settings, "cclZeIpcExchange", "sockets")
    environment["CCL_ATL_TRANSPORT"] = _get_setting(xpu_settings, "cclAtlTransport", "ofi")
    environment["FI_PROVIDER"] = _get_setting(xpu_settings, "fiProvider", "tcp")
    environment["VLLM_WORKER_MULTIPROC_METHOD"] = _get_setting(xpu_settings, "workerMultiprocessMethod", "spawn")
    environment.pop("ZE_AFFINITY_MASK", None)

    if sys.platform.startswith("linux"):
        python_environment = Path(python_executable).absolute().parent.parent
        environment_library_directory = python_environment / "lib"
        environment_library_directory.mkdir(parents=True, exist_ok=True)
        loader_path = _find_level_zero_loader()
        if loader_path is None:
            raise RuntimeError("The Intel XPU runtime requires libze_loader.so or libze_loader.so.1 on Linux.")

        compatibility_loader = environment_library_directory / "libze_loader.so"
        if not compatibility_loader.exists() and not compatibility_loader.is_symlink():
            compatibility_loader.symlink_to(loader_path)

        environment["CCL_ROOT"] = str(python_environment)
        from ctypes.util import find_library

        gpu_driver = find_library("ze_intel_gpu")
        if gpu_driver:
            environment["ZE_ENABLE_ALT_DRIVERS"] = gpu_driver
        else:
            environment.pop("ZE_ENABLE_ALT_DRIVERS", None)
        environment.pop("UR_ADAPTERS_FORCE_LOAD", None)
        environment.pop("ZES_ENABLE_SYSMAN", None)

        loader_paths = [str(environment_library_directory)]
        current_path = environment.get("PATH", "")
        ocloc_root = Path(environment.get("ESI_OCLOC_ROOT", str(Path.home() / ".local" / "opt" / "intel-ocloc")))
        ocloc_bin_directory = ocloc_root / "usr" / "bin"
        ocloc_library_directory = ocloc_root / "usr" / "lib" / "x86_64-linux-gnu"
        if (ocloc_library_directory / "libocloc.so").is_file() and ocloc_bin_directory.is_dir():
            current_path = _join_unique_paths(str(ocloc_bin_directory), current_path)
            loader_paths.append(str(ocloc_library_directory))

        environment["PATH"] = current_path
        environment["LD_LIBRARY_PATH"] = _join_unique_paths(
            *loader_paths,
            environment.get("LD_LIBRARY_PATH", ""),
        )

    return environment


def _exec_inference_server() -> None:
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--esi-xpu-settings", required=True)
    parser.add_argument("--esi-xpu-graph", choices=("0", "1"), required=True)
    parser.add_argument("--esi-xpu-bf16-mtp-draft", choices=("0", "1"), required=True)
    options, server_arguments = parser.parse_known_args()
    if server_arguments and server_arguments[0] == "--":
        server_arguments = server_arguments[1:]
    if not server_arguments:
        parser.error("the inference server script and its arguments are required")

    settings = json.loads(options.esi_xpu_settings)
    if not isinstance(settings, dict):
        parser.error("the XPU runtime settings must be a JSON object")
    environment = build_xpu_process_environment(
        dict(os.environ),
        sys.executable,
        settings,
        options.esi_xpu_graph == "1",
        options.esi_xpu_bf16_mtp_draft == "1",
    )
    os.execve(sys.executable, [sys.executable, *server_arguments], environment)


if __name__ == "__main__":
    _exec_inference_server()
