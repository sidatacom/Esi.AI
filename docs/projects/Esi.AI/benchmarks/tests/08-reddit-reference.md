# Reddit reference

**Evidence type: external reference. This was not measured by Esi.AI or by this session.**

- Source: Intel Arc Reddit post and linked reproduction guide
- Backend: vLLM XPU
- vLLM version: `0.27.2rc1.dev77+gac7509e2b.xpu`
- XPU kernels: `0.1.12.3`
- Model: Qwen3.8-27B GPTQ INT4 G128
- KV cache: FP8
- Speculative decoding: MTP4
- Graph/eager: XPU graph enabled
- Concurrency: 1
- Prompt/output: 512 / 128 tokens
- Warmup: one same-shape warmup
- Measured runs: five
- Result: **84.65 tok/s** median
- Note: External reference, not measured through Esi.AI Studio.
