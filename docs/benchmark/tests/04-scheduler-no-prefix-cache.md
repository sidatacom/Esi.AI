# Scheduler without prefix cache

- Backend: vLLM XPU
- Model: Qwen3.8-27B GPTQ INT4 G128
- Target dtype: FP16
- KV cache: FP8
- Speculative decoding: MTP4
- Graph/eager: eager
- Scheduler: `max_num_seqs=64`, `max_num_batched_tokens=8192`
- Prefix caching: disabled and confirmed in the resolved vLLM configuration
- Prompt/output: 512 / 128 tokens
- Result: **24.15 tok/s** median
