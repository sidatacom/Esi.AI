import asyncio
import io
import unittest
from types import SimpleNamespace

import inference_pb2
from PIL import Image

from esi_multimodal_utils import (
    build_multimodal_chat_messages,
    close_images,
    get_engine_model_config,
    supports_image_input,
)


def _png(color):
    buffer = io.BytesIO()
    Image.new("RGB", (1, 1), color).save(buffer, format="PNG")
    return buffer.getvalue()


class MultimodalUtilsTests(unittest.TestCase):
    def test_get_engine_model_config_usesModelConfigAttribute(self):
        model_config = object()

        result = asyncio.run(get_engine_model_config(SimpleNamespace(model_config=model_config)))

        self.assertIs(model_config, result)

    def test_get_engine_model_config_fallsBackToLegacyGetter(self):
        model_config = object()
        engine = SimpleNamespace(get_model_config=lambda: model_config)

        result = asyncio.run(get_engine_model_config(engine))

        self.assertIs(model_config, result)

    def test_get_engine_model_config_awaitsAsyncConfiguration(self):
        model_config = object()

        async def get_model_config():
            return model_config

        result = asyncio.run(
            get_engine_model_config(SimpleNamespace(model_config=get_model_config()))
        )

        self.assertIs(model_config, result)

    def test_supports_image_input_requires_vision_config(self):
        vision_config = SimpleNamespace(vision_config=object())
        text_config = {"hf_config": {"model_type": "text-only"}}

        self.assertTrue(supports_image_input(SimpleNamespace(hf_config=vision_config)))
        self.assertFalse(supports_image_input(text_config))

    def test_build_multimodal_chat_messages_preservesOrderedTextAndImages(self):
        message = inference_pb2.ChatMessage(role="user", content="Describe this.")
        message.images.add(media_type="image/png", data=_png((255, 0, 0)))
        message.images.add(media_type="image/png", data=_png((0, 0, 255)))
        message.content_parts.add(text="First: ")
        message.content_parts.add(image_index=1)
        message.content_parts.add(text=" then ")
        message.content_parts.add(image_index=0)

        chat_messages, ordered_images, decoded_images = build_multimodal_chat_messages([message])
        try:
            self.assertEqual(
                [
                    {"type": "text", "text": "First: "},
                    {"type": "image"},
                    {"type": "text", "text": " then "},
                    {"type": "image"},
                ],
                chat_messages[0]["content"],
            )
            self.assertEqual((0, 0, 255), ordered_images[0].getpixel((0, 0)))
            self.assertEqual((255, 0, 0), ordered_images[1].getpixel((0, 0)))
        finally:
            close_images(decoded_images)

    def test_build_multimodal_chat_messages_PreservesTextOnlyMessageShape(self):
        message = inference_pb2.ChatMessage(role="user", content="Hello")

        chat_messages, ordered_images, decoded_images = build_multimodal_chat_messages([message])
        try:
            self.assertEqual("Hello", chat_messages[0]["content"])
            self.assertEqual([], ordered_images)
            self.assertEqual([], decoded_images)
        finally:
            close_images(decoded_images)

    def test_build_multimodal_chat_messages_RejectsUnreferencedImage(self):
        message = inference_pb2.ChatMessage(role="user", content="Look")
        message.images.add(media_type="image/png", data=_png((0, 255, 0)))
        message.content_parts.add(text="Look")

        with self.assertRaisesRegex(ValueError, "Every attached image"):
            build_multimodal_chat_messages([message])


if __name__ == "__main__":
    unittest.main()
