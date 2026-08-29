#!/usr/bin/env python3
"""Local gRPC bridge for vLLM's AsyncLLMEngine."""

from __future__ import annotations

import argparse
import asyncio
import contextlib
import gc
import inspect
import json
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
                from vllm import AsyncEngineArgs, AsyncLLMEngine

                engine_options = {
                    "model": request.model_path,
                    "max_model_len": request.max_model_len or None,
                    "tensor_parallel_size": request.tensor_parallel_size or 1,
                    "trust_remote_code": request.trust_remote_code,
                    "enforce_eager": request.enforce_eager,
                }
                if request.gpu_memory_utilization > 0:
                    engine_options["gpu_memory_utilization"] = request.gpu_memory_utilization
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
                    error=f"vLLM failed to load: {exception}\n{traceback.format_exc()}"
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
            try:
                payload = {
                    "model": self._model_id,
                    "messages": [{"role": message.role, "content": message.content} for message in request.messages],
                    "max_tokens": request.max_tokens or 512,
                    "temperature": request.temperature if request.temperature > 0 else 0.7,
                    "top_p": request.top_p if request.top_p > 0 else 0.9,
                }
                response = await asyncio.to_thread(self._post_json, "/v1/chat/completions", payload)
                content = response["choices"][0]["message"].get("content", "")
                yield inference_pb2.GenerateResponse(delta=content, finished=True)
            except Exception as exception:
                yield inference_pb2.GenerateResponse(error=f"SGLang generation failed: {exception}")
            return

        request_id = request.request_id or str(uuid.uuid4())
        started = time.monotonic()
        previous_text = ""
        try:
            prompt = await self._format_prompt(request.messages)
            from vllm import SamplingParams

            sampling_params = SamplingParams(
                max_tokens=request.max_tokens or 512,
                temperature=request.temperature if request.temperature > 0 else 0.7,
                top_p=request.top_p if request.top_p > 0 else 0.9,
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
            yield inference_pb2.GenerateResponse(
                error=f"vLLM generation failed: {exception}\n{traceback.format_exc()}"
            )

    async def _format_prompt(self, messages) -> str:
        tokenizer = self._engine.get_tokenizer()
        if inspect.isawaitable(tokenizer):
            tokenizer = await tokenizer
        chat_messages = [{"role": message.role, "content": message.content} for message in messages]
        if hasattr(tokenizer, "apply_chat_template"):
            return tokenizer.apply_chat_template(chat_messages, tokenize=False, add_generation_prompt=True)
        return "\n".join(f"{message['role']}: {message['content']}" for message in chat_messages) + "\nassistant:"

    async def _abort(self, request_id: str) -> None:
        if self._engine is not None:
            with contextlib.suppress(Exception):
                await self._engine.abort(request_id)

    async def _load_sglang(self, request, context):
        command = [
            sys.executable,
            "-m",
            "sglang.launch_server",
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
                    error=f"SGLang exited with code {process.returncode}: {(output or b'').decode(errors='replace')[-2000:]}"
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
