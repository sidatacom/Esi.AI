# Esi.AI.PyTorch

The Studio page runs QLoRA for `Qwen/Qwen3-4B-Instruct-2507` in a local Python process. On Linux, prepare an Intel XPU-enabled environment and point Studio at that interpreter:

```bash
python3 -m venv ~/.venvs/esi-pytorch-xpu
source ~/.venvs/esi-pytorch-xpu/bin/activate
python -m pip install --upgrade pip
python -m pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/xpu
python -m pip install -r src/Esi.AI/Esi.AI.PyTorch/Python/requirements-xpu.txt
```

Configure the executable in Studio's settings, for example:

```json
{
  "PyTorchTraining": {
    "PythonExecutable": "/home/user/.venvs/esi-pytorch-xpu/bin/python",
    "OutputDirectory": "/home/user/.local/share/Esi.AI/PyTorch/runs"
  }
}
```

The trainer checks `torch.xpu.is_available()` before downloading/loading the model. The first run downloads the model from Hugging Face. Adapter weights are written to the configured output directory; temporary run state and progress are held by the Studio host. Install a PyTorch wheel compatible with the installed Intel GPU driver and verify the environment with `python -c "import torch; print(torch.xpu.is_available())"` before starting a run.