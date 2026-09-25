# Optimization lab native KV

**Evidence type: external reference. This was not measured by Esi.AI or by this session.**

- Source: steveseguin/b70-optimization-lab
- Backend: vLLM XPU
- Model: Qwen3.8-27B GPTQ INT4 G128
- KV cache: native FP16 (`auto`)
- Speculative decoding: MTP4
- Graph/eager: XPU graph enabled
- Scheduler: one user, batch budget 8192
- Prompt/output: 512 / 128 tokens
- Result: **87.6054 tok/s**
- Evidence note: The source labels this GPTQ checkpoint quality-rejected after a deterministic Python-result canary.
