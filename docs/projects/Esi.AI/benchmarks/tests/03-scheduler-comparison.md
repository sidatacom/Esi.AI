# Scheduler comparison

- Backend: vLLM XPU
- Model: Qwen3.8-27B GPTQ INT4 G128
- Target dtype: FP16
- KV cache: FP8
- Speculative decoding: MTP4
- Graph/eager: eager
- Scheduler: `max_num_seqs=64`, `max_num_batched_tokens=8192`
- Prompt/output: 512 / 128 tokens
- Result: **25.28 tok/s** median
- Note: Prefix-cache resolution was not yet confirmed for this run.
