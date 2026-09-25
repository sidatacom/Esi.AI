# Direct eager MTP4

- Backend: Esi.AI direct gRPC bridge to vLLM XPU
- vLLM version: `0.28.0`
- Model: Qwen3.8-27B GPTQ INT4 G128
- Target dtype: FP16
- KV cache: FP8
- Speculative decoding: MTP4
- Graph/eager: XPU graph disabled, eager enabled
- Scheduler: `max_num_seqs=1`, `max_num_batched_tokens=8192`
- Maximum context: `131072`
- Result: **21.765 tok/s** median for 132 generated tokens
