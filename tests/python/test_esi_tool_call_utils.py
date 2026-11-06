import unittest

from esi_tool_call_utils import parse_tool_calls_for_chat_template


class ParseToolCallsForChatTemplateTests(unittest.TestCase):
    def test_parses_openai_argument_json_string_as_mapping(self):
        tool_calls_json = (
            '[{"id":"call_1","type":"function","function":'
            '{"name":"lookup","arguments":"{\\"query\\":\\"weather\\"}"}}]'
        )

        tool_calls = parse_tool_calls_for_chat_template(tool_calls_json)

        self.assertEqual({"query": "weather"}, tool_calls[0]["function"]["arguments"])

    def test_preserves_mapping_arguments(self):
        tool_calls_json = (
            '[{"id":"call_1","type":"function","function":'
            '{"name":"lookup","arguments":{"query":"weather"}}}]'
        )

        tool_calls = parse_tool_calls_for_chat_template(tool_calls_json)

        self.assertEqual({"query": "weather"}, tool_calls[0]["function"]["arguments"])

    def test_rejects_non_object_arguments(self):
        tool_calls_json = (
            '[{"id":"call_1","type":"function","function":'
            '{"name":"lookup","arguments":"[]"}}]'
        )

        with self.assertRaisesRegex(ValueError, "arguments must be a JSON object"):
            parse_tool_calls_for_chat_template(tool_calls_json)

    def test_rejects_invalid_argument_json(self):
        tool_calls_json = (
            '[{"id":"call_1","type":"function","function":'
            '{"name":"lookup","arguments":"not-json"}}]'
        )

        with self.assertRaisesRegex(ValueError, "has invalid JSON arguments"):
            parse_tool_calls_for_chat_template(tool_calls_json)
