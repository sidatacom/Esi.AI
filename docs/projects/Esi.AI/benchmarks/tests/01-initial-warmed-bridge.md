# Initial warmed bridge

- Backend: vLLM XPU
- vLLM version: `0.28.0`
- Model: Qwen3.8-27B GPTQ INT4 G128
- Target dtype: FP16
- KV cache: FP8
- Speculative decoding: MTP4
- Graph/eager: not fully recorded
- Prompt/output: 576 / 128 tokens
- Result: **44.81 tok/s** median
- Scope: historical local bridge benchmark; several scheduler details were not preserved.
