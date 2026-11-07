"""Shared image decoding and chat-message normalization for local vLLM bridges."""

from __future__ import annotations

import inspect
import math
from io import BytesIO


MAX_IMAGE_BYTES = 20 * 1024 * 1024


def output_tokens_per_second(metrics) -> float:
    """Return vLLM's per-request decode rate, excluding prompt prefill."""
    if metrics is None:
        return 0.0

    try:
        token_count = int(_get_value(metrics, "num_generation_tokens"))
        first_token_ts = float(_get_value(metrics, "first_token_ts"))
        last_token_ts = float(_get_value(metrics, "last_token_ts"))
    except (OverflowError, TypeError, ValueError):
        return 0.0

    decode_duration = last_token_ts - first_token_ts
    if (
        token_count <= 1
        or not math.isfinite(first_token_ts)
        or not math.isfinite(last_token_ts)
        or decode_duration <= 0
    ):
        return 0.0

    tokens_per_second = (token_count - 1) / decode_duration
    return tokens_per_second if math.isfinite(tokens_per_second) else 0.0


async def get_engine_model_config(engine):
    """Return the model config exposed by supported vLLM engine APIs."""
    model_config = getattr(engine, "model_config", None)
    if model_config is None:
        get_model_config = getattr(engine, "get_model_config", None)
        if not callable(get_model_config):
            raise AttributeError("The vLLM engine exposes no model configuration.")
        model_config = get_model_config()
    if inspect.isawaitable(model_config):
        model_config = await model_config
    return model_config


def supports_image_input(model_config) -> bool:
    """Return whether the loaded model configuration exposes a vision config."""
    hf_config = _get_value(model_config, "hf_config")
    return _get_value(hf_config, "vision_config") is not None


def build_multimodal_chat_messages(messages):
    """Create chat-template messages and decoded images in prompt order."""
    if any(message.images for message in messages):
        from PIL import Image, ImageOps

    chat_messages = []
    ordered_images = []
    decoded_images = []
    completed = False
    try:
        for message in messages:
            local_images = []
            for image in message.images:
                media_type = image.media_type.strip().lower()
                if not media_type.startswith("image/"):
                    raise ValueError("An image content part has an invalid media type.")
                if not image.data:
                    raise ValueError("An image content part contains no data.")
                if len(image.data) > MAX_IMAGE_BYTES:
                    raise ValueError("An image content part exceeds the 20 MiB limit.")
                try:
                    with Image.open(BytesIO(image.data)) as source:
                        decoded = ImageOps.exif_transpose(source).convert("RGB")
                except OSError as exception:
                    raise ValueError("An image content part could not be decoded.") from exception
                decoded_images.append(decoded)
                local_images.append(decoded)

            if not local_images:
                content = message.content
            else:
                content = []
                if message.content_parts:
                    referenced_images = set()
                    for part in message.content_parts:
                        kind = part.WhichOneof("part")
                        if kind == "text":
                            content.append({"type": "text", "text": part.text})
                        elif kind == "image_index":
                            image_index = part.image_index
                            if image_index >= len(local_images):
                                raise ValueError("An image content part refers to an unavailable image.")
                            referenced_images.add(image_index)
                            content.append({"type": "image"})
                            ordered_images.append(local_images[image_index])
                        else:
                            raise ValueError("A chat content part must contain text or an image index.")
                    if referenced_images != set(range(len(local_images))):
                        raise ValueError("Every attached image must appear in the ordered chat content.")
                else:
                    if message.content:
                        content.append({"type": "text", "text": message.content})
                    for image in local_images:
                        content.append({"type": "image"})
                        ordered_images.append(image)

            chat_message = {"role": message.role, "content": content}
            if message.tool_calls_json:
                from esi_tool_call_utils import parse_tool_calls_for_chat_template

                chat_message["tool_calls"] = parse_tool_calls_for_chat_template(message.tool_calls_json)
            if message.tool_call_id:
                chat_message["tool_call_id"] = message.tool_call_id
            chat_messages.append(chat_message)
        completed = True
        return chat_messages, ordered_images, decoded_images
    finally:
        if not completed:
            close_images(decoded_images)


def close_images(images) -> None:
    for image in images:
        image.close()


def _get_value(value, name):
    if isinstance(value, dict):
        return value.get(name)
    return getattr(value, name, None)
