# Current Request

Verify whether the Qwen models used by Esi.AI match the Qwen3.8-27B model described in the linked OpenVINO article.

# Action plan

- [done] Inspect local Qwen model files and configured model sources.
- [done] Compare the discovered model identity and format with the article.
- [done] Run the smallest relevant OpenVINO validation if a local Qwen artifact exists.
- [done] Record the result and identify the matching IR prerequisite.

# Findings

LocalAI contains Qwen3.8 GGUF models, including `Qwen3.8-9B-Q8_0.gguf` and `Qwen3.8-27B-Q4_K_M.gguf`. The 9B header reports architecture `qwen35`, confirming it is a Qwen3.8 model. Esi.AI tested the 9B file on `GPU.1`; native OpenVINO failed during `LLMPipeline.Create` with status `-17`, matching the earlier SmolLM2 failure. The linked article uses the pre-converted `Qwen3.8-27B-int4-ov` OpenVINO IR directory through `VLMPipeline`, not direct GGUF loading. The remaining validation requires that IR model directory or a runtime that supports direct `qwen35` GGUF loading.

The linked IR model card requires OpenVINO 2026.4.0 or newer GenAI nightly builds, while Esi.AI currently references OpenVINO and GenAI 2026.3.0. The Qwen3.8 GGUF files are therefore the same model family but not the article's OpenVINO artifact, and the current native failure is expected until the runtime and model format are aligned.
