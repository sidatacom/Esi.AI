"""Compatibility hooks for vLLM XPU environments on mixed-vendor hosts."""

from types import ModuleType
from functools import wraps
import importlib.abc
import importlib.machinery
import sys


def disable_cuda_platform_probe() -> None:
    """Prevent vLLM 0.28.0 from activating CUDA from host-wide NVML data."""
    module_name = "vllm.third_party.pynvml"
    if module_name in sys.modules:
        return

    pynvml = ModuleType(module_name)
    pynvml.nvmlInit = lambda: None
    pynvml.nvmlDeviceGetCount = lambda: 0
    pynvml.nvmlShutdown = lambda: None
    sys.modules[module_name] = pynvml


def enable_xpu_memory_probe_fallback() -> None:
    """Use the XPU total memory when Torch cannot report free memory."""
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
