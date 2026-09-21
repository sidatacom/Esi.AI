#!/usr/bin/env python3
"""Local gRPC bridge for vLLM's AsyncLLMEngine."""

from __future__ import annotations

import argparse
import asyncio
import contextlib
import gc
import inspect
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
import traceback
import urllib.error
import urllib.request
import uuid

import grpc

import inference_pb2
import inference_pb2_grpc


def _extract_root_error(output: str) -> str:
    lines = output.splitlines()
    exception_start = -1
    for index in range(len(lines) - 1, -1, -1):
        candidate = lines[index].strip()
        separator = candidate.find(": ")
        if separator <= 0:
            continue
        exception_type = candidate[:separator]
        if exception_type.endswith(("Error", "Exception", "Failure")):
            exception_start = index
            break

    if exception_start < 0:
        return next((line.strip() for line in reversed(lines) if line.strip()), "The Python backend exited without an error message.")

    error_lines = [lines[exception_start].strip()]
    for line in lines[exception_start + 1:]:
        if line.strip().startswith("["):
            break
        if line.strip():
            error_lines.append(line.strip())
    return "\n".join(error_lines)


def _enable_b70_bf16_mtp_draft() -> None:
    """Enable unquantized Qwen3.5-family MTP layers for preserved-BF16 checkpoints."""
    import vllm

    model_path = Path(vllm.__file__).parent / "model_executor" / "models" / "qwen3_5_mtp.py"
    if not model_path.exists():
        raise RuntimeError(f"B70 BF16 MTP patch requires {model_path}")

    source = model_path.read_text()
    marker = "B70_MTP_NIGHTLY_DRAFT"
    if marker in source:
        return

    old = (
        "        original_quant = vllm_config.quant_config\n"
        '        if quant_config and quant_config.get_name() not in ("modelopt_fp4",):\n'
        "            hf_qc = getattr(model_config.hf_config, \"quantization_config\", None)\n"
        "            if isinstance(hf_qc, dict):\n"
        '                dynamic = hf_qc.get("dynamic", {})\n'
        '                if any(k.startswith("-:") and "mtp" in k for k in dynamic):\n'
        "                    vllm_config.quant_config = None\n"
    )
    new = (
        "        original_quant = vllm_config.quant_config\n"
        '        if quant_config and quant_config.get_name() not in ("modelopt_fp4",):\n'
        '            if os.environ.get("B70_MTP_BF16_DRAFT") == "1":\n'
        '                print("[B70] MTP draft: using unquantized BF16 layers", flush=True)\n'
        "                vllm_config.quant_config = None\n"
        "            else:\n"
        "                hf_qc = getattr(model_config.hf_config, \"quantization_config\", None)\n"
        "                if isinstance(hf_qc, dict):\n"
        '                    dynamic = hf_qc.get("dynamic", {})\n'
        '                    if any(k.startswith("-:") and "mtp" in k for k in dynamic):\n'
        "                        vllm_config.quant_config = None\n"
        "        # B70_MTP_NIGHTLY_DRAFT\n"
    )
    if source.count(old) != 1:
        raise RuntimeError(
            f"B70 BF16 MTP patch anchor was not found exactly once in {model_path}; "
            "refusing to modify an incompatible vLLM installation"
        )

    patched = source.replace(old, new, 1)
    if "\nimport os\n" not in patched and not patched.startswith("import os\n"):
        if "import torch\n" not in patched:
            raise RuntimeError(f"B70 BF16 MTP patch could not find an import anchor in {model_path}")
        patched = patched.replace("import torch\n", "import os\nimport torch\n", 1)
    compile(patched, str(model_path), "exec")
    model_path.write_text(patched)


if os.environ.get("VLLM_TARGET_DEVICE", "").lower() == "xpu":
    import vllm_xpu_bootstrap

    vllm_xpu_bootstrap.disable_cuda_platform_probe()
    vllm_xpu_bootstrap.enable_xpu_memory_probe_fallback()
    vllm_xpu_bootstrap.install_vllm_memory_probe_hook()


class InferenceService(inference_pb2_grpc.InferenceServicer):
    def __init__(self, backend_port: int) -> None:
        self._engine = None
        self._sglang_process = None
        self._backend_port = backend_port
        self._model_id = ""
        self._load_lock = asyncio.Lock()

    async def CheckReadiness(self, request, context):
        return inference_pb2.ReadinessResponse(
            ready=True,
            model_loaded=self._engine is not None or self._sglang_process is not None,
            model_id=self._model_id,
        )

    async def LoadModel(self, request, context):
        if not request.model_path.strip():
            return inference_pb2.ModelOperationResponse(error="A model path is required.")

        async with self._load_lock:
            await self._unload_model()
            if request.engine.lower() == "sglang":
                return await self._load_sglang(request, context)
            if request.engine.lower() != "vllm":
                return inference_pb2.ModelOperationResponse(error=f"Unsupported engine '{request.engine}'.")
            try:
                if request.enable_bf16_mtp_draft:
                    _enable_b70_bf16_mtp_draft()
                if os.environ.get("VLLM_TARGET_DEVICE", "").lower() == "xpu":
                    import vllm_xpu_bootstrap

                    vllm_xpu_bootstrap.disable_cuda_platform_probe()
                    vllm_xpu_bootstrap.enable_xpu_memory_probe_fallback()
                    vllm_xpu_bootstrap.install_vllm_memory_probe_hook()
                from vllm import AsyncEngineArgs, AsyncLLMEngine

                engine_options = {
                    "model": request.model_path,
                    "max_model_len": request.max_model_len or None,
                    "tensor_parallel_size": request.tensor_parallel_size or 1,
                    "trust_remote_code": request.trust_remote_code,
                    "enforce_eager": request.enforce_eager,
                }
                if request.quantization:
                    engine_options["quantization"] = request.quantization
                if request.dtype:
                    engine_options["dtype"] = request.dtype
                if request.kv_cache_dtype:
                    engine_options["kv_cache_dtype"] = request.kv_cache_dtype
                if request.speculative_config_json:
                    engine_options["speculative_config"] = json.loads(request.speculative_config_json)
                if request.max_num_seqs:
                    engine_options["max_num_seqs"] = request.max_num_seqs
                if request.max_num_batched_tokens:
                    engine_options["max_num_batched_tokens"] = request.max_num_batched_tokens
                engine_options["enable_prefix_caching"] = request.enable_prefix_caching
                if request.gpu_memory_utilization > 0:
                    engine_options["gpu_memory_utilization"] = request.gpu_memory_utilization
                if request.enable_bf16_mtp_draft:
                    os.environ["B70_MTP_BF16_DRAFT"] = "1"
                self._engine = AsyncLLMEngine.from_engine_args(AsyncEngineArgs(**engine_options))
                self._model_id = request.model_path
                return inference_pb2.ModelOperationResponse(
                    succeeded=True,
                    model_id=self._model_id,
                )
            except ImportError as exception:
                return inference_pb2.ModelOperationResponse(
                    error=(
                        "The selected Python environment does not provide vLLM. "
                        "Install grpcio, protobuf and vllm in that environment: "
                        f"{exception}"
                    )
                )
            except Exception as exception:
                return inference_pb2.ModelOperationResponse(
                    error=_extract_root_error(f"{exception}\n{traceback.format_exc()}")
                )

    async def UnloadModel(self, request, context):
        async with self._load_lock:
            await self._unload_model()
        return inference_pb2.ModelOperationResponse(succeeded=True)

    async def Generate(self, request, context):
        if self._engine is None and self._sglang_process is None:
            yield inference_pb2.GenerateResponse(error="No vLLM model is loaded.")
            return

        if self._sglang_process is not None:
            started = time.monotonic()
            try:
                payload = {
                    "model": self._model_id,
                    "messages": self._chat_messages(request.messages),
                    "max_tokens": request.max_tokens or 512,
                    "temperature": request.temperature,
                    "top_p": request.top_p,
                    "top_k": request.top_k,
                    "min_p": request.min_p,
                    "repetition_penalty": request.repetition_penalty or 1.0,
                    "seed": request.seed or None,
                    "stop": list(request.stop_sequences) or None,
                }
                tools = self._tool_definitions(request.tools)
                if tools:
                    payload["tools"] = tools
                tool_choice = self._tool_choice(request.tool_choice_json)
                if tool_choice is not None:
                    payload["tool_choice"] = tool_choice
                response = await asyncio.to_thread(self._post_json, "/v1/chat/completions", payload)
                message = response["choices"][0]["message"]
                content = message.get("content") or ""
                tool_calls = message.get("tool_calls") or []
                usage = response.get("usage") or {}
                generated_tokens = int(usage.get("completion_tokens") or 0)
                prompt_tokens = int(usage.get("prompt_tokens") or 0)
                elapsed = time.monotonic() - started
                yield inference_pb2.GenerateResponse(
                    delta=content,
                    finished=True,
                    generated_tokens=generated_tokens,
                    prompt_tokens=prompt_tokens,
                    tokens_per_second=generated_tokens / elapsed if elapsed > 0 else 0,
                    tool_calls_json=json.dumps(tool_calls, separators=(",", ":")),
                )
            except Exception as exception:
                yield inference_pb2.GenerateResponse(error=f"SGLang generation failed: {exception}")
            return

        request_id = request.request_id or str(uuid.uuid4())
        decode_started = None
        previous_text = ""
        try:
            tool_choice = self._tool_choice(request.tool_choice_json)
            if request.tools and tool_choice != "none":
                yield inference_pb2.GenerateResponse(
                    error=(
                        "Structured tool calls are not supported by the direct vLLM engine path. "
                        "Use the SGLang/OpenAI-compatible path or a backend with a native tool parser."
                    )
                )
                return
            prompt = await self._format_prompt(request)
            from vllm import SamplingParams

            sampling_params = SamplingParams(
                max_tokens=request.max_tokens or 512,
                temperature=request.temperature,
                top_p=request.top_p,
                top_k=request.top_k or -1,
                min_p=request.min_p,
                repetition_penalty=request.repetition_penalty or 1.0,
                seed=request.seed or None,
                stop=list(request.stop_sequences) or None,
            )
            async for output in self._engine.generate(prompt, sampling_params, request_id):
                if context.cancelled():
                    await self._abort(request_id)
                    return
                if decode_started is None:
                    decode_started = time.monotonic()
                result = output.outputs[0]
                current_text = result.text
                delta = current_text[len(previous_text):]
                previous_text = current_text
                if delta:
                    yield inference_pb2.GenerateResponse(
                        delta=delta,
                        generated_tokens=len(result.token_ids),
                        prompt_tokens=len(output.prompt_token_ids),
                    )
                if output.finished:
                    elapsed = time.monotonic() - (decode_started or time.monotonic())
                    yield inference_pb2.GenerateResponse(
                        finished=True,
                        generated_tokens=len(result.token_ids),
                        prompt_tokens=len(output.prompt_token_ids),
                        tokens_per_second=len(result.token_ids) / elapsed if elapsed > 0 else 0,
                    )
                    return
        except asyncio.CancelledError:
            await self._abort(request_id)
            raise
        except Exception as exception:
            yield inference_pb2.GenerateResponse(
                error=f"vLLM generation failed: {exception}\n{traceback.format_exc()}"
            )

    async def _format_prompt(self, request) -> str:
        tokenizer = self._engine.get_tokenizer()
        if inspect.isawaitable(tokenizer):
            tokenizer = await tokenizer
        chat_messages = self._chat_messages(request.messages)
        tools = self._tool_definitions(request.tools)
        tool_choice = self._tool_choice(request.tool_choice_json)
        if tool_choice == "none":
            tools = []
        if hasattr(tokenizer, "apply_chat_template"):
            template_options = {
                "tokenize": False,
                "add_generation_prompt": True,
            }
            if tools:
                template_options["tools"] = tools
            if tool_choice is not None and tool_choice != "none":
                template_options["tool_choice"] = tool_choice
            return tokenizer.apply_chat_template(chat_messages, **template_options)
        return "\n".join(f"{message['role']}: {message['content']}" for message in chat_messages) + "\nassistant:"

    @staticmethod
    def _chat_messages(messages):
        result = []
        for message in messages:
            item = {"role": message.role, "content": message.content}
            if message.tool_calls_json:
                item["tool_calls"] = json.loads(message.tool_calls_json)
            if message.tool_call_id:
                item["tool_call_id"] = message.tool_call_id
            result.append(item)
        return result

    @staticmethod
    def _tool_definitions(tools):
        result = []
        for tool in tools:
            function = {
                "name": tool.name,
                "description": tool.description,
                "parameters": json.loads(tool.parameters_json or "{}"),
            }
            result.append({"type": tool.type or "function", "function": function})
        return result

    @staticmethod
    def _tool_choice(value):
        if not value:
            return None
        return json.loads(value)

    async def _abort(self, request_id: str) -> None:
        if self._engine is not None:
            with contextlib.suppress(Exception):
                await self._engine.abort(request_id)

    async def _load_sglang(self, request, context):
        devices = [device.strip() for device in request.devices if device.strip()]
        if not devices and request.device.strip():
            devices = [request.device.strip()]
        vendors = {device.split(":", 1)[0].lower() for device in devices}
        if len(vendors) != 1:
            return inference_pb2.ModelOperationResponse(
                error="CUDA and XPU devices cannot be mixed in one SGLang worker."
            )

        bridge_directory = os.path.dirname(os.path.abspath(__file__))
        bootstrap = (
            f"import sys; sys.path.insert(0, {json.dumps(bridge_directory)}); "
            "import runpy; import vllm_xpu_bootstrap; "
            "vllm_xpu_bootstrap.enable_sglang_xpu_memory_probe_fallback(); "
            "vllm_xpu_bootstrap.enable_sglang_xpu_eager_sampling_fallback(); "
            "runpy.run_module('sglang.launch_server', run_name='__main__')"
        )
        command = [
            sys.executable,
            "-c",
            bootstrap,
            "--model-path",
            request.model_path,
            "--host",
            "127.0.0.1",
            "--port",
            str(self._backend_port),
            "--context-length",
            str(request.max_model_len or 262144),
            "--tp-size",
            str(request.tensor_parallel_size or 1),
        ]
        if "xpu" in vendors:
            command.extend(["--device", "xpu", "--attention-backend", "torch_native"])
        if request.gpu_memory_utilization > 0:
            command.extend(["--mem-fraction-static", str(request.gpu_memory_utilization)])
        if request.trust_remote_code:
            command.append("--trust-remote-code")
        self._sglang_process = await asyncio.create_subprocess_exec(
            *command,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.STDOUT,
        )
        while context is None or not context.cancelled():
            if self._sglang_process.returncode is not None:
                process = self._sglang_process
                self._sglang_process = None
                output, _ = await process.communicate()
                return inference_pb2.ModelOperationResponse(
                    error=_extract_root_error((output or b"").decode(errors="replace"))
                )
            try:
                models = await asyncio.to_thread(self._get_json, "/v1/models")
                model_id = models["data"][0]["id"]
                self._model_id = model_id
                return inference_pb2.ModelOperationResponse(succeeded=True, model_id=model_id)
            except (OSError, KeyError, IndexError, json.JSONDecodeError):
                await asyncio.sleep(.25)
        await self._unload_model()
        return inference_pb2.ModelOperationResponse(error="SGLang model loading was cancelled.")

    async def _unload_model(self) -> None:
        if self._engine is not None:
            engine = self._engine
            self._engine = None
            self._model_id = ""
            shutdown = getattr(engine, "shutdown_background_loop", None)
            if shutdown is not None:
                result = shutdown()
                if inspect.isawaitable(result):
                    await result
            del engine
            gc.collect()
        if self._sglang_process is None:
            return
        process = self._sglang_process
        self._sglang_process = None
        self._model_id = ""
        if process.returncode is None:
            process.terminate()
            with contextlib.suppress(asyncio.TimeoutError):
                await asyncio.wait_for(process.wait(), timeout=2)
        if process.returncode is None:
            process.kill()
            await process.wait()

    def _get_json(self, path):
        with urllib.request.urlopen(f"http://127.0.0.1:{self._backend_port}{path}", timeout=2) as response:
            return json.loads(response.read())

    def _post_json(self, path, payload):
        body = json.dumps(payload).encode()
        request = urllib.request.Request(
            f"http://127.0.0.1:{self._backend_port}{path}",
            data=body,
            headers={"Content-Type": "application/json"},
        )
        with urllib.request.urlopen(request, timeout=300) as response:
            return json.loads(response.read())


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--engine", choices=("vllm", "sglang"), default="vllm")
    parser.add_argument("--model")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--grpc-port", type=int, default=8000)
    return parser.parse_args()


async def run(options: argparse.Namespace) -> None:
    server = grpc.aio.server()
    if options.grpc_port >= 65535:
        raise ValueError("The gRPC port must leave one port available for the SGLang compatibility process.")
    service = InferenceService(options.grpc_port + 1)
    inference_pb2_grpc.add_InferenceServicer_to_server(service, server)
    bound_port = server.add_insecure_port(f"{options.host}:{options.grpc_port}")
    if bound_port == 0:
        raise RuntimeError(f"Could not bind the local gRPC server to {options.host}:{options.grpc_port}.")
    await server.start()

    if options.model:
        await service.LoadModel(
            inference_pb2.LoadModelRequest(model_path=options.model, engine=options.engine),
            None,
        )

    stopped = asyncio.Event()
    loop = asyncio.get_running_loop()
    for signum in (signal.SIGINT, signal.SIGTERM):
        with contextlib.suppress(NotImplementedError):
            loop.add_signal_handler(signum, stopped.set)
    try:
        await stopped.wait()
    finally:
        await service.UnloadModel(inference_pb2.UnloadModelRequest(), None)
        await server.stop(grace=2)


def main() -> int:
    try:
        asyncio.run(run(parse_args()))
    except KeyboardInterrupt:
        return 0
    except Exception as exception:
        print(f"inference bridge failed: {exception}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
