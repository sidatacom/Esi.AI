#!/usr/bin/env python3
"""Local gRPC bridge for vLLM's asynchronous CUDA inference engine."""

from __future__ import annotations

import argparse
import asyncio
import contextlib
import gc
import inspect
import json
import os
import signal
import sys
import time
import traceback
import uuid

import grpc

import inference_pb2
import inference_pb2_grpc


def _enable_bf16_mtp_draft() -> None:
    """Enable unquantized Qwen3.5-family MTP layers for preserved-BF16 checkpoints."""
    import vllm

    model_path = os.path.join(os.path.dirname(vllm.__file__), "model_executor", "models", "qwen3_5_mtp.py")
    if not os.path.exists(model_path):
        raise RuntimeError(f"BF16 MTP patch requires {model_path}")

    with open(model_path, encoding="utf-8") as model_file:
        source = model_file.read()
    marker = "ESI_CUDA12_BF16_MTP_DRAFT"
    if marker in source:
        return

    original = (
        "        original_quant = vllm_config.quant_config\n"
        '        if quant_config and quant_config.get_name() not in ("modelopt_fp4",):\n'
        "            hf_qc = getattr(model_config.hf_config, \"quantization_config\", None)\n"
        "            if isinstance(hf_qc, dict):\n"
        '                dynamic = hf_qc.get("dynamic", {})\n'
        '                if any(k.startswith("-:") and "mtp" in k for k in dynamic):\n'
        "                    vllm_config.quant_config = None\n"
    )
    replacement = (
        "        original_quant = vllm_config.quant_config\n"
        '        if quant_config and quant_config.get_name() not in ("modelopt_fp4",):\n'
        '            if os.environ.get("B70_MTP_BF16_DRAFT") == "1":\n'
        '                print("[Esi.AI] MTP draft: using unquantized BF16 layers", flush=True)\n'
        "                vllm_config.quant_config = None\n"
        "            else:\n"
        "                hf_qc = getattr(model_config.hf_config, \"quantization_config\", None)\n"
        "                if isinstance(hf_qc, dict):\n"
        '                    dynamic = hf_qc.get("dynamic", {})\n'
        '                    if any(k.startswith("-:") and "mtp" in k for k in dynamic):\n'
        "                        vllm_config.quant_config = None\n"
        "        # ESI_CUDA12_BF16_MTP_DRAFT\n"
    )
    if source.count(original) != 1:
        raise RuntimeError(f"BF16 MTP patch anchor was not found exactly once in {model_path}")
    patched = source.replace(original, replacement, 1)
    if "\nimport os\n" not in patched and not patched.startswith("import os\n"):
        if "import torch\n" not in patched:
            raise RuntimeError(f"BF16 MTP patch could not find an import anchor in {model_path}")
        patched = patched.replace("import torch\n", "import os\nimport torch\n", 1)
    compile(patched, model_path, "exec")
    with open(model_path, "w", encoding="utf-8") as model_file:
        model_file.write(patched)


class InferenceService(inference_pb2_grpc.InferenceServicer):
    def __init__(self) -> None:
        self._engine = None
        self._model_id = ""
        self._load_lock = asyncio.Lock()

    async def CheckReadiness(self, request, context):
        return inference_pb2.ReadinessResponse(
            ready=True,
            model_loaded=self._engine is not None,
            model_id=self._model_id,
        )

    async def LoadModel(self, request, context):
        if not request.model_path.strip():
            return inference_pb2.ModelOperationResponse(error="A model path is required.")
        if request.engine.lower() != "vllm":
            return inference_pb2.ModelOperationResponse(error=f"Unsupported engine '{request.engine}'.")

        async with self._load_lock:
            await self._unload_model()
            try:
                if request.enable_bf16_mtp_draft:
                    _enable_bf16_mtp_draft()
                    os.environ["B70_MTP_BF16_DRAFT"] = "1"
                from vllm import AsyncEngineArgs, AsyncLLMEngine

                engine_options = {
                    "model": request.model_path,
                    "max_model_len": request.max_model_len or None,
                    "tensor_parallel_size": request.tensor_parallel_size or 1,
                    "trust_remote_code": request.trust_remote_code,
                    "enforce_eager": request.enforce_eager,
                    "enable_prefix_caching": request.enable_prefix_caching,
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
                if request.gpu_memory_utilization > 0:
                    engine_options["gpu_memory_utilization"] = request.gpu_memory_utilization
                self._engine = AsyncLLMEngine.from_engine_args(AsyncEngineArgs(**engine_options))
                self._model_id = request.model_path
                return inference_pb2.ModelOperationResponse(succeeded=True, model_id=self._model_id)
            except ImportError as exception:
                return inference_pb2.ModelOperationResponse(
                    error=f"The Python environment does not provide vLLM and its gRPC dependencies: {exception}"
                )
            except Exception as exception:
                return inference_pb2.ModelOperationResponse(error=f"{exception}\n{traceback.format_exc()}")

    async def UnloadModel(self, request, context):
        async with self._load_lock:
            await self._unload_model()
        return inference_pb2.ModelOperationResponse(succeeded=True)

    async def Generate(self, request, context):
        if self._engine is None:
            yield inference_pb2.GenerateResponse(error="No vLLM model is loaded.")
            return

        tool_choice = self._tool_choice(request.tool_choice_json)
        if request.tools and tool_choice != "none":
            yield inference_pb2.GenerateResponse(
                error="Structured tool calls are not supported by the direct vLLM engine path."
            )
            return

        request_id = request.request_id or str(uuid.uuid4())
        started = time.monotonic()
        previous_text = ""
        try:
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
                    elapsed = time.monotonic() - started
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
            yield inference_pb2.GenerateResponse(error=f"vLLM generation failed: {exception}\n{traceback.format_exc()}")

    async def _format_prompt(self, request) -> str:
        tokenizer = self._engine.get_tokenizer()
        if inspect.isawaitable(tokenizer):
            tokenizer = await tokenizer
        chat_messages = self._chat_messages(request.messages)
        tools = self._tool_definitions(request.tools)
        if self._tool_choice(request.tool_choice_json) == "none":
            tools = []
        if hasattr(tokenizer, "apply_chat_template"):
            template_options = {"tokenize": False, "add_generation_prompt": True}
            if tools:
                template_options["tools"] = tools
            if self._tool_choice(request.tool_choice_json) not in (None, "none"):
                template_options["tool_choice"] = self._tool_choice(request.tool_choice_json)
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
        return [
            {
                "type": tool.type or "function",
                "function": {
                    "name": tool.name,
                    "description": tool.description,
                    "parameters": json.loads(tool.parameters_json or "{}"),
                },
            }
            for tool in tools
        ]

    @staticmethod
    def _tool_choice(value):
        return json.loads(value) if value else None

    async def _abort(self, request_id: str) -> None:
        if self._engine is not None:
            with contextlib.suppress(Exception):
                await self._engine.abort(request_id)

    async def _unload_model(self) -> None:
        if self._engine is None:
            return
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


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--grpc-port", type=int, default=8000)
    return parser.parse_args()


async def run(options: argparse.Namespace) -> None:
    if options.grpc_port < 1 or options.grpc_port > 65535:
        raise ValueError("The gRPC port must be between 1 and 65535.")
    server = grpc.aio.server()
    service = InferenceService()
    inference_pb2_grpc.add_InferenceServicer_to_server(service, server)
    if server.add_insecure_port(f"{options.host}:{options.grpc_port}") == 0:
        raise RuntimeError(f"Could not bind the local gRPC server to {options.host}:{options.grpc_port}.")
    await server.start()

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
        print(f"vLLM inference bridge failed: {exception}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())