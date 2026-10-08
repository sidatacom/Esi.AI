# Qwen3.8 Backend Benchmark

## Ziel

Diese Benchmark-Vorlage standardisiert den Eingabetext, die Sampling-Optionen und die clientseitige Messung. Die Profile verwenden backendgerechte Qwen3.8-Artefakte; sie sind wegen unterschiedlicher Quantisierungen und Chat-Templates nicht bit-identisch. Prompt- und Completion-Tokenzahlen deshalb immer mitprotokollieren.

## Prompt und Runner

Der Runner baut fuer jeden Request dieselbe Nachricht auf. Nur die feste Run-ID aendert sich, damit Prefix-Caches nicht denselben Request wiederverwenden. Die Kontextfuellung besteht aus `hello `-Wiederholungen; im aktuellen Qwen-Tokenizer entsprach eine Wiederholung einem Token. Massgeblich bleibt `usage.prompt_tokens`, nicht die Schaetzung.

Die feste, einheitliche Request-Vorlage hat die Form eines einzelnen Copilot-Coding-Agent-Auftrags. Der lange Kontext bleibt absichtlich synthetische Token-Fuellung; der Lauf testet Streaming und Kontextaufnahme, aber keine Tool-Aufrufe oder echte Repository-Aenderungen:

```text
ESI-QWEN38-AGENT-BENCH-V1
You are GitHub Copilot, an autonomous coding agent working in a VS Code repository.
Run: <eindeutige Run-ID>
Repository context padding (synthetic token-fill; no source files or tools are supplied):
<"hello " genau N-mal wiederholen>
Agent request:
Investigate a streaming-cancellation bug in a C# service: cancelling a client request must stop generation, release backend resources, and preserve caller cancellation semantics. Trace the owning code path, propose the smallest root-cause fix, and include a focused regression test. Do not invent APIs or claim that tools were run. Continue with concrete patch and test details until the response token limit; do not end with an early summary.
```

Der Runner setzt `N` und Run-ID reproduzierbar. Bei manuellen Laeufen dieselbe Vorlage, dieselben Wiederholungszahlen und eine fortlaufende Run-ID verwenden.

Kurzbenchmark mit 390 Kontextwiederholungen, einem Warm-up und fuenf Messlaeufen. Das ist eine feste Eingabevorlage, keine Zusicherung einer backenduebergreifend identischen Tokenzahl:

```sh
node benchmarks/qwen38-benchmark.mjs \
  --model <MODEL_CONFIGURATION_ID> \
  --repeat-count 390 \
  --warmups 1 \
  --runs 5
```

128k-Kontextprobe, ein einzelner Lauf ohne langen Warm-up:

```sh
node benchmarks/qwen38-benchmark.mjs \
  --model <MODEL_CONFIGURATION_ID> \
  --repeat-count 127850 \
  --warmups 0 \
  --runs 1
```

`--model` ist die ID des ausgewaehlten Studio-Konfigurationsprofils. `ESI_STUDIO_URL` kann die Basis-URL ueberschreiben; Standard ist `http://localhost:7010`.

Der Runner sendet immer `max_tokens=128`, `temperature=0`, `top_p=1`, `top_k=1`, `min_p=0`, `repetition_penalty=1`, `frequency_penalty=0`, `presence_penalty=0`, `seed=42`, `reasoning_effort=none`, Streaming und Usage-Daten. Keine Tools, Bilder, Systemnachrichten, Chat-Historie oder parallelen Requests verwenden. `reasoning_effort=none` wird aktuell nur im OpenVINO-Adapter explizit auf Qwens `enable_thinking=false` abgebildet; der Prompt untersagt zusaetzlich Reasoning fuer alle Backends. Falls ein Backend dennoch Think-Tokens ausgibt, den Lauf als nicht direkt vergleichbar markieren.

Die vLLM-Profile speichern zusaetzlich Runtime-Defaults fuer Temperatur und Top-P. Der Runner uebergibt die Benchmarkwerte explizit pro Request; die gespeicherten vLLM-Defaults duerfen fuer Messungen nicht anstelle dieser Request-Werte verwendet werden.

## Messablauf

1. Nur ein Modell/Backend gleichzeitig laden. Vor jedem Lauf Backend-Variante, Modellpfad, Geraeteroute und Profil-ID notieren.
2. Kurzbenchmark: den Runner mit `repeat-count 390`, `warmups 1` und `runs 5` ausfuehren. Die erste Antwort ist Warm-up und geht nicht in den Median ein.
3. 128k-Probe: nach erfolgreichem Kurzbenchmark pro Backend hoechstens einen Lauf mit `repeat-count 127850`, ohne Warm-up. Sicherstellen, dass `promptTokens + 128 <= 131072`; sonst fuer **alle** Backend-Laeufe denselben niedrigeren Repeat-Count verwenden.
4. Jede Zeile des Runners enthaelt Usage, Finish-Reason, clientseitige TTFT, clientseitige Decode-Rate und Server-Rate. Fuer den Backendvergleich primaer den Median von `clientDecodeTokensPerSecond` aus den fuenf Kurzlaeufen verwenden; Serverraten sind backendseitig unterschiedlich definiert. Ein Lauf zaehlt nur als vollstaendig, wenn `completionLengthMatchesLimit=true` und `fits131072Context=true`.
5. Die 128k-Probe separat ausweisen: Prompt-Tokens, TTFT, Decode-Rate, Wall-Zeit, verfuegbarer RAM und Swap vor/nach dem Lauf. Nicht mit dem Kurzbenchmark-Median vermischen.

Wegen des zuvor beobachteten Speicherdrucks keine parallelen 128k-Anfragen starten. Vorher mindestens 16 GiB verfuegbaren RAM anstreben; bei weniger als 4 GiB verfuegbar, stark wachsendem Swap oder einem Runtime-Fehler sofort stoppen und nicht blind wiederholen. Nach jedem Backend das Modell entladen und Ressourcen erholen lassen.

## Studio-Profile

Der All-Backend-Prompt verwendet pro Backend zusaetzlich den exakten Konfigurationsnamen `Benchmark`. Die folgende Tabelle dokumentiert den zuvor angelegten Profilbestand; diese Profile werden nicht umbenannt oder ueberschrieben. Falls noch kein passendes `Benchmark`-Profil existiert, wird ein separates Profil mit dem backendgerechten Qwen3.8-27B-Artefakt angelegt.

Die aufgefuehrten Bestandsprofile heissen `Benchmark - Qwen3.8 - <Variante>`, haben `AutoLaunch=false` und setzen, soweit die Runtime dies anbietet, Kontext auf 131072 sowie Sampling auf die Werte des Runners. Backend-spezifische Load-Optionen bleiben in der jeweiligen Konfiguration.

| Profil | Studio-Variante / Route | Qwen3.8-Modell | Backend-Loadwerte | Status |
|---|---|---|---|---|
| Benchmark - Qwen3.8 - Llama Vulkan | `llama.vulkan`, Intel BMG `Vulkan1` | `/home/llm/.cache/esi-ai/models/Qwen3.8-27B-UD-Q4_K_S.gguf` | Kontext 131072, alle moeglichen GPU-Layer | Gespeichert |
| Benchmark - Qwen3.8 - Llama SYCL | `llama.sycl`, Intel BMG `SYCL0` | `/home/llm/.cache/esi-ai/models/Qwen3.8-27B-UD-Q4_K_S.gguf` | Kontext 131072, alle moeglichen GPU-Layer | Gespeichert; Runtime bereit |
| Benchmark - Qwen3.8 - Llama CUDA12 | `llama.cuda12`, RTX 4070 `CUDA0` | `/home/llm/.cache/esi-ai/models/Qwen3.8-27B-UD-Q4_K_S.gguf` | Kontext 131072, alle moeglichen GPU-Layer | Gespeichert; Runtime bereit |
| Benchmark - Qwen3.8 - OpenVINO | `openvino`, Intel BMG `GPU.1` | `/home/llm/.cache/esi-ai/models/Qwen3.8-27B-Uncensored-int4-awq-g128-ov` | 128 neue Tokens, Sampling aus, Temperatur 0, Top-P 1, Top-K 1, Repetition-Penalty 1 | Gespeichert |
| Benchmark - Qwen3.8 - vLLM XPU | `vllm.xpu`, Intel BMG `xpu:0` | `/home/llm/.cache/esi-ai/models/Qwen3.8-27B-GPTQ-Int4-sym-G128-MTP-BF16` | Max-Model-Length 131072, eine Sequenz, Batch 8192, GPTQ, FP16, FP8-KV, Prefix-Cache aus, MTP aus, XPU-Graph aus; Runtime-Defaults Temperatur 0.7/Top-P 0.9 werden pro Request ueberschrieben | Gespeichert; Runtime bereit |
| Benchmark - Qwen3.8 - vLLM CUDA12 | `vllm.cuda12`, RTX 4070 `cuda:0` | `/home/llm/.cache/esi-ai/models/Qwen3.8-27B-GPTQ-Int4-sym-G128-MTP-BF16` | Max-Model-Length 131072, eine Sequenz, Batch 8192, GPTQ, FP16, FP8-KV, Prefix-Cache aus, MTP aus | **Nicht gespeichert** |

vLLM CUDA12 ist aktuell nicht benchmarkbereit: Studio meldet fehlende Pakete `grpc`, `google.protobuf` und `vllm` im Environment `/home/llm/.venvs/esi-ai-vllm`; ausserdem meldet PyTorch dort keinen CUDA-Accelerator. Das Anlegen des separaten Profils scheiterte im Studio-Create-Dialog, der den sichtbaren Namen nicht an seine Validierung uebergab. Es wurde kein bestehendes Profil ueberschrieben und keine Paketinstallation gestartet. Llama CUDA12 ist davon unabhaengig und meldet RTX/CUDA0 als bereit.

SGLang und DotLlm sind in der aktuellen Studio-Registrierung keine startbaren Runtime-Varianten und erhalten deshalb kein irrefuehrendes Profil. Ein optimierter vLLM-XPU-Lauf mit MTP4 ist ein separater Benchmark-Modus und darf nicht mit den Baseline-Profilen vermischt werden.