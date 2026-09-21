# Exact local p512/g128

- Backend: vLLM XPU
- Model: Qwen3.8-27B GPTQ INT4 G128
- Target dtype: FP16
- KV cache: FP8
- Speculative decoding: MTP4
- Scheduler: `max_num_seqs=1`, `max_num_batched_tokens=8192`
- Prompt/output: 512 / 128 tokens
- Warmup: one
- Measured runs: five
- Result: **55.02 tok/s** median
- Note: This is the strongest recorded local baseline before the later graph/eager direct comparison.
