# Qwen3.8-27B Benchmark Overview

Hardware: one Intel Arc Pro B70 32 GB.

Model: `Qwen3.8-27B-GPTQ-Int4-sym-G128-MTP-BF16`.

| Test | Model | Backend / runtime | KV | MTP | Graph | Scheduler | Shape | Result |
|---|---|---|---|---|---|---|---|---:|
| [Initial warmed bridge](tests/01-initial-warmed-bridge.md) | Qwen3.8-27B GPTQ INT4 G128 | vLLM XPU 0.28.0 | FP8 | MTP4 | Not fully recorded | Not fully recorded | 576 / 128 | 44.81 tok/s |
| [Exact local p512/g128](tests/02-exact-local-p512-g128.md) | Qwen3.8-27B GPTQ INT4 G128 | vLLM XPU | FP8 | MTP4 | Not fully recorded | `max_num_seqs=1`, batch 8192 | 512 / 128 | 55.02 tok/s |
| [Scheduler comparison](tests/03-scheduler-comparison.md) | Qwen3.8-27B GPTQ INT4 G128 | vLLM XPU | FP8 | MTP4 | Eager | `max_num_seqs=64`, batch 8192 | 512 / 128 | 25.28 tok/s |
| [Scheduler without prefix cache](tests/04-scheduler-no-prefix-cache.md) | Qwen3.8-27B GPTQ INT4 G128 | vLLM XPU | FP8 | MTP4 | Eager | `max_num_seqs=64`, batch 8192 | 512 / 128 | 24.15 tok/s |
| [Graph MTP4](tests/05-graph-mtp4.md) | Qwen3.8-27B GPTQ INT4 G128 | vLLM XPU 0.28.0 | FP8 | MTP4 | Enabled | `max_num_seqs=1`, batch 8192 | 132 output tokens | 46.862 tok/s |
| [Eager MTP4](tests/06-eager-mtp4.md) | Qwen3.8-27B GPTQ INT4 G128 | vLLM XPU 0.28.0 | FP8 | MTP4 | Disabled | `max_num_seqs=1`, batch 8192 | 132 output tokens | 21.765 tok/s |
| [Graph MTP2](tests/07-graph-mtp2.md) | Qwen3.8-27B GPTQ INT4 G128 | vLLM XPU 0.28.0 | FP8 | MTP2 | Enabled | `max_num_seqs=1`, batch 8192 | 132 output tokens | 51.857 tok/s |

## External references and optimization notes

These reports are not local Studio measurements and are not directly comparable to the table above.

| Report | Evidence | Result |
|---|---|---:|
| [Reddit reference](tests/08-reddit-reference.md) | External vLLM XPU reference, five measured runs | 84.65 tok/s median |
| [Optimization lab FP8 KV](tests/09-optimization-lab-fp8.md) | External result; quality-rejected checkpoint | 83.7019 tok/s |
| [Optimization lab native KV](tests/10-optimization-lab-native-kv.md) | External result; quality-rejected checkpoint | 87.6054 tok/s |

## Shared local parameters

- GPU memory utilization: `0.88`
- Maximum context: `131072`
- Prefix caching: disabled for the local direct runs
- Temperature: `0`
- Target dtype: FP16
- MTP draft: BF16 tensors on disk, loaded as FP16 at runtime
- Local model path: `/home/llm/.cache/esi-ai/models/Qwen3.8-27B-GPTQ-Int4-sym-G128-MTP-BF16`

Only local measurements from this Esi.AI session are listed above. External reference measurements are intentionally excluded from this overview. Studio was not running during the recorded local tests.
