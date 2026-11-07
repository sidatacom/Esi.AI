import math
import unittest
from types import SimpleNamespace

from esi_multimodal_utils import output_tokens_per_second


class OutputTokensPerSecondTests(unittest.TestCase):
    def test_uses_decode_interval_and_excludes_first_token(self):
        metrics = SimpleNamespace(
            num_generation_tokens=16,
            first_token_ts=100.0,
            last_token_ts=102.0,
        )

        self.assertEqual(7.5, output_tokens_per_second(metrics))

    def test_returns_unavailable_for_single_token(self):
        metrics = SimpleNamespace(
            num_generation_tokens=1,
            first_token_ts=100.0,
            last_token_ts=100.0,
        )

        self.assertEqual(0.0, output_tokens_per_second(metrics))

    def test_returns_unavailable_for_missing_or_invalid_metrics(self):
        invalid_metrics = SimpleNamespace(
            num_generation_tokens=16,
            first_token_ts=math.nan,
            last_token_ts=102.0,
        )

        self.assertEqual(0.0, output_tokens_per_second(None))
        self.assertEqual(0.0, output_tokens_per_second(invalid_metrics))
