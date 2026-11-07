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
import sys
import time
import traceback
import uuid

from esi_process_lifecycle import install_parent_lifetime_handlers
from esi_multimodal_utils import (
    build_multimodal_chat_messages,
    close_images,
    get_engine_model_config,
    output_tokens_per_second,
    supports_image_input,
)
from esi_reasoning_utils import add_reasoning_effort_template_options

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
    def __init__(self) -> None:
        self._engine = None
        self._model_id = ""
        self._supports_image_input = False
        self._speculative_decoding_enabled = False
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

        async with self._load_lock:
            await self._unload_model()
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
                    vllm_xpu_bootstrap.enable_xpu_graph_dynamo_compatibility()
                from vllm import AsyncEngineArgs, AsyncLLMEngine

                engine_options = {
                    "model": request.model_path,
                    "max_model_len": request.max_model_len or None,
                    "tensor_parallel_size": request.tensor_parallel_size or 1,
                    "trust_remote_code": request.trust_remote_code,
                    "enforce_eager": request.enforce_eager,
                }
                if os.environ.get("VLLM_TARGET_DEVICE", "").lower() == "xpu":
                    engine_options["compilation_config"] = {
                        "custom_ops": ["all", "-silu_and_mul"]
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
                model_config = await get_engine_model_config(self._engine)
                self._supports_image_input = supports_image_input(model_config)
                self._model_id = request.model_path
                self._speculative_decoding_enabled = bool(request.speculative_config_json)
                return inference_pb2.ModelOperationResponse(
                    succeeded=True,
                    model_id=self._model_id,
                    supports_image_input=self._supports_image_input,
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
        if self._engine is None:
            yield inference_pb2.GenerateResponse(error="No vLLM model is loaded.")
            return
        if not self._supports_image_input and any(message.images for message in request.messages):
            yield inference_pb2.GenerateResponse(error="The loaded vLLM model does not support image input.")
            return

        request_id = request.request_id or str(uuid.uuid4())
        previous_text = ""
        images_to_close = []
        try:
            prompt, images_to_close = await self._format_prompt(request)
            from vllm import SamplingParams

            sampling_params = SamplingParams(
                max_tokens=request.max_tokens or 512,
                temperature=request.temperature,
                top_p=request.top_p,
                top_k=request.top_k or -1,
                # vLLM does not support nonzero min_p with speculative decoding.
                min_p=0.0 if self._speculative_decoding_enabled else request.min_p,
                repetition_penalty=request.repetition_penalty or 1.0,
                seed=request.seed or None,
                stop=list(request.stop_sequences) or None,
            )
            if request.structured_output_json_schema:
                from vllm.sampling_params import StructuredOutputsParams

                sampling_params.structured_outputs = StructuredOutputsParams(
                    json=json.loads(request.structured_output_json_schema)
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
                    yield inference_pb2.GenerateResponse(
                        finished=True,
                        generated_tokens=len(result.token_ids),
                        prompt_tokens=len(output.prompt_token_ids),
                        tokens_per_second=output_tokens_per_second(
                            getattr(output, "metrics", None)
                        ),
                    )
                    return
        except asyncio.CancelledError:
            await self._abort(request_id)
            raise
        except Exception as exception:
            yield inference_pb2.GenerateResponse(
                error=f"vLLM generation failed: {exception}\n{traceback.format_exc()}"
            )
        finally:
            close_images(images_to_close)

    async def _format_prompt(self, request):
        tokenizer = self._engine.get_tokenizer()
        if inspect.isawaitable(tokenizer):
            tokenizer = await tokenizer
        chat_messages, image_inputs, images_to_close = build_multimodal_chat_messages(request.messages)
        tools = self._tool_definitions(request.tools)
        tool_choice = self._tool_choice(request.tool_choice_json)
        if tool_choice == "none":
            tools = []
        completed = False
        try:
            if hasattr(tokenizer, "apply_chat_template"):
                template_options = {
                    "tokenize": False,
                    "add_generation_prompt": True,
                }
                if tools:
                    template_options["tools"] = tools
                if tool_choice is not None and tool_choice != "none":
                    template_options["tool_choice"] = tool_choice
                add_reasoning_effort_template_options(
                    template_options, tokenizer, request.reasoning_effort
                )
                prompt = tokenizer.apply_chat_template(chat_messages, **template_options)
                if image_inputs:
                    prompt = {
                        "prompt": prompt,
                        "multi_modal_data": {
                            "image": image_inputs[0] if len(image_inputs) == 1 else image_inputs
                        },
                    }
            else:
                if image_inputs:
                    raise ValueError("The loaded vLLM model has no chat template for image input.")
                prompt = "\n".join(
                    f"{message['role']}: {message['content']}" for message in chat_messages
                ) + "\nassistant:"
            completed = True
            return prompt, images_to_close
        finally:
            if not completed:
                close_images(images_to_close)

    @staticmethod
    def _tool_definitions(tools):
        return [
            {
                "name": tool.name,
                "description": tool.description,
                "parameters": json.loads(tool.parameters_json or "{}"),
            }
            for tool in tools
        ]

    @staticmethod
    def _tool_choice(value):
        if not value:
            return None
        return json.loads(value)

    async def _abort(self, request_id: str) -> None:
        if self._engine is not None:
            with contextlib.suppress(Exception):
                await self._engine.abort(request_id)

    async def _unload_model(self) -> None:
        self._speculative_decoding_enabled = False
        self._supports_image_input = False
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

def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--grpc-port", type=int, default=8000)
    return parser.parse_args()


async def run(options: argparse.Namespace) -> None:
    stopped = asyncio.Event()
    install_parent_lifetime_handlers(stopped)

    server = grpc.aio.server(options=[("grpc.max_receive_message_length", 64 * 1024 * 1024)])
    service = InferenceService()
    inference_pb2_grpc.add_InferenceServicer_to_server(service, server)
    bound_port = server.add_insecure_port(f"{options.host}:{options.grpc_port}")
    if bound_port == 0:
        raise RuntimeError(f"Could not bind the local gRPC server to {options.host}:{options.grpc_port}.")
    await server.start()

    if options.model:
        await service.LoadModel(
            inference_pb2.LoadModelRequest(model_path=options.model, engine="vllm"),
            None,
        )

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
