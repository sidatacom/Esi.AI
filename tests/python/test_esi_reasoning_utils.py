import unittest

from esi_reasoning_utils import add_reasoning_effort_template_options


class Tokenizer:
    def __init__(self, chat_template):
        self.chat_template = chat_template


class AddReasoningEffortTemplateOptionsTests(unittest.TestCase):
    def test_none_disables_thinking_when_template_supports_it(self):
        template_options = {"tokenize": False}
        tokenizer = Tokenizer("{{ enable_thinking }}")

        add_reasoning_effort_template_options(template_options, tokenizer, "none")

        self.assertFalse(template_options["enable_thinking"])

    def test_effort_enables_thinking_and_normalizes_high_to_xhigh(self):
        template_options = {}
        tokenizer = Tokenizer(
            {
                "default": "{{ enable_thinking }} {{ reasoning_effort }}",
                "tool_use": "{{ enable_thinking }}",
            }
        )

        add_reasoning_effort_template_options(template_options, tokenizer, "HIGH")

        self.assertTrue(template_options["enable_thinking"])
        self.assertEqual("xhigh", template_options["reasoning_effort"])

    def test_unsupported_template_keeps_its_default_behavior(self):
        template_options = {"tokenize": False}
        tokenizer = Tokenizer("{{ messages }}")

        add_reasoning_effort_template_options(template_options, tokenizer, "none")

        self.assertEqual({"tokenize": False}, template_options)

    def test_rejects_unsupported_effort(self):
        with self.assertRaisesRegex(ValueError, "Unsupported reasoning effort"):
            add_reasoning_effort_template_options({}, Tokenizer("{{ enable_thinking }}"), "extreme")
