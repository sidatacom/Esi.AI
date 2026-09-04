# Current Request

Download and test the official OpenVINO IR model `OpenVINO/Qwen3.8-27B-int4-ov` from Hugging Face and determine the required Esi.AI runtime and pipeline changes.

# Action plan

- [completed] Check disk space, Hugging Face tooling, and repository file metadata.
- [completed] Download the complete model into the Esi.AI model cache.
- [completed] Verify the downloaded IR structure and record the runtime compatibility requirement.
- [completed] Test the model with the compatible native runtime and report the result.

# Results

- Model cache path: `/home/llm/.cache/esi-ai/models/Qwen3.8-27B-int4-ov`
- Hugging Face manifest: `expected=26 actual=26 missing=0 mismatched=0 extra=0`
- Model contents include language, tokenizer, detokenizer, vision embeddings, vision position, and vision merger IR components.
- Model requirement: OpenVINO 2026.4.0 or newer and OpenVINO GenAI nightly from 2026-08-14 or newer.
- OpenVINO Ubuntu 26 archive: `https://storage.openvinotoolkit.org/repositories/openvino/packages/nightly/2026.4.0-22468-47d6e3f7031/openvino_toolkit_ubuntu26_2026.4.0.dev20260714_x86_64.tgz` (65,014,329 bytes; SHA-256 verified).
- GenAI Ubuntu 26 archive used for the successful test: `https://storage.openvinotoolkit.org/repositories/openvino_genai/packages/nightly/2026.4.0.0.dev20260814/openvino_genai_ubuntu26_2026.4.0.0.dev20260814_x86_64.tar.gz` (94,726,217 bytes; SHA-256 verified).
- The July 14 GenAI archive was also downloaded and verified, but it predates the model's August 14 compatibility requirement.
- Esi.AI now selects `VLMPipeline` when `openvino_vision_embeddings_model.xml` is present and keeps `LLMPipeline` for text-only IR models.
- The native integration test passed with `GPU.1`, the August 14 GenAI nightly, and the downloaded model: 1 passed in 38.8 seconds.
- The existing stable Esi.AI NuGet references remain at 2026.3.0 because the tested 2026.4 runtime is distributed as native nightly archives.
