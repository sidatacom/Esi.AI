# OpenVINO GenAI mit GGUF-Modellen von Hugging Face

## Kurzfassung

OpenVINO GenAI kann bestimmte GGUF-Modelle direkt laden. Eine GGUF-Datei ist dabei ein einzelnes Modellartefakt, das Modellgewichte und die benötigten Metadaten enthält. Für unterstützte Architekturen ist keine vorherige Konvertierung in OpenVINO IR erforderlich.

Die direkte GGUF-Unterstützung wurde mit OpenVINO GenAI 2025.2 als Preview eingeführt und ist auf bestimmte Modellarchitekturen begrenzt. Für nicht unterstützte Architekturen ist weiterhin ein OpenVINO-IR-Modell über Optimum Intel der empfohlene Weg.

Der verlinkte Intel-Beitrag nennt als validierte Modellfamilien unter anderem Qwen2.5, Llama 3.1/3.2 und DeepSeek-R1-Distill-Qwen. Die getesteten Quantisierungen umfassen `Q4_0`, `Q4_K_M`, `Q8_0` und `FP16`. Die GPU-Unterstützung hängt dabei von Quantisierung, OpenVINO-Version und Intel-GPU-Generation ab.

Die Unterstützung ist versionsabhängig. Im ursprünglichen 2025.2-Beitrag war Qwen3 noch nicht unterstützt. Ein späteres OpenVINO-GenAI-Update beschreibt Qwen3-Unterstützung sowie GGUF-Tokenizer und Detokenizer. Daher muss die konkrete installierte Runtime-Version zusammen mit der Modellarchitektur betrachtet werden.

## Modell von Hugging Face herunterladen

Zuerst wird eine konkrete `.gguf`-Datei aus einem Hugging-Face-Repository heruntergeladen. Das Repository kann mehrere Quantisierungen und Varianten enthalten; die gewünschte Datei muss deshalb explizit ausgewählt werden.

```bash
python -m pip install huggingface_hub openvino-genai
hf auth login
hf download <owner>/<repository> <model-file>.gguf --local-dir ./models/<model-name>
```

Bei öffentlichen Repositories ist `hf auth login` nicht immer erforderlich. Bei gated Modellen muss die Lizenz auf Hugging Face akzeptiert und ein gültiges Zugriffstoken eingerichtet werden.

Beispielhafte lokale Struktur:

```text
models/
└── <model-name>/
    └── <model-file>.gguf
```

Für den direkten GGUF-Weg ist die `.gguf`-Datei der relevante Modellpfad. Ein `openvino_model.xml`/`openvino_model.bin`-Paar wird für das initiale Laden nicht benötigt.

## Zwei GGUF-Ladevarianten

Der Blog beschreibt zwei Entwicklungsstände des GGUF-Readers:

### 2025.2: GGUF plus OpenVINO-Tokenizer

Im ursprünglichen C++-Beispiel wird neben der GGUF-Datei ein OpenVINO-Tokenizer-Verzeichnis übergeben:

```cpp
ov::genai::Tokenizer tokenizer(tokenizer_path);
ov::genai::LLMPipeline pipe(gguf_path, tokenizer, "GPU");
```

Der Grund ist, dass die Online-Konvertierung des Tokenizers in dieser Version noch nicht verfügbar war. Als Workaround wurden `openvino_tokenizer.xml`, `openvino_tokenizer.bin`, `openvino_detokenizer.xml` und `openvino_detokenizer.bin` separat heruntergeladen oder mit `convert_tokenizer` erzeugt.

### Spätere Versionen: GGUF als einziger Eingang

Spätere OpenVINO-GenAI-Versionen unterstützen GGUF-Tokenizer und Detokenizer direkt aus der GGUF-Datei. Der Blog zeigt dafür:

```cpp
ov::AnyMap pipe_config = {};
pipe_config["enable_save_ov_model"] = true;
pipe_config.insert({ov::cache_dir("llm_cache")});
ov::genai::LLMPipeline pipe(gguf_path, "GPU", pipe_config);
```

Beim ersten Laden wird der OpenVINO-Graph aus GGUF erzeugt. Mit `enable_save_ov_model` kann die erzeugte OpenVINO-Repräsentation anschließend gespeichert und bei späteren Ladevorgängen wiederverwendet werden. Der genaue Parametername und die Verfügbarkeit müssen gegen die installierte GenAI-Version geprüft werden.

## Mit OpenVINO GenAI laden

Die OpenVINO-Pipeline erhält den Pfad zur GGUF-Datei und das gewünschte OpenVINO-Gerät:

```python
import openvino_genai as ov_genai

pipeline = ov_genai.LLMPipeline(
    "./models/<model-name>/<model-file>.gguf",
    "GPU"
)

config = ov_genai.GenerationConfig()
config.max_new_tokens = 100

result = pipeline.generate("The Sun is yellow because", config)
print(result)
```

Für eine konkrete GPU kann der OpenVINO-Gerätename verwendet werden, zum Beispiel `GPU.1`. Mehrere GPUs können über einen unterstützten `MULTI`-Gerätepfad angesprochen werden, sofern die verwendete OpenVINO-Version und das Modell diesen Pfad unterstützen.

Tokenizer, Detokenizer und Token-Auswahl werden bei GenAI teilweise auf der CPU verarbeitet; das bedeutet nicht, dass die Modellinferenz nicht auf der GPU läuft.

## Unterstützte Modellarchitektur prüfen

GGUF bedeutet nicht automatisch, dass jede GGUF-Datei mit OpenVINO GenAI geladen werden kann. Vor der Integration sollte geprüft werden:

1. Unterstützt die verwendete OpenVINO-GenAI-Version die Architektur des Modells?
2. Ist die Datei tatsächlich eine Textgenerations-GGUF-Datei und keine Embedding-, Vision- oder andere Spezialdatei?
3. Läuft dieselbe Datei mit einem minimalen `LLMPipeline`-Test auf dem gewünschten Gerät?
4. Sind OpenVINO GenAI und die nativen Runtime-Bibliotheken installiert und versionskompatibel?

Ein Fehler beim direkten GGUF-Laden ist daher nicht automatisch ein Treiberfehler. Er kann auch bedeuten, dass die Architektur in der Preview nicht unterstützt wird. In diesem Fall sollte das ursprüngliche Hugging-Face-Modell in OpenVINO IR exportiert werden.

### Qwen3.8 aus dem lokalen LocalAI-Bestand

Im LocalAI-Modellbestand liegen Qwen3.8-GGUF-Dateien, zum Beispiel `Qwen3.8-9B-Q8_0.gguf` und `Qwen3.8-27B-Q4_K_M.gguf`. Ihre GGUF-Metadaten melden die Architektur `qwen35`. Ein realer Esi.AI-Test mit `Qwen3.8-9B-Q8_0.gguf` auf `GPU.1` scheitert in OpenVINO 2026.3 beim Erzeugen der `LLMPipeline` mit Status `-17`.

Das ist nicht das Artefakt aus dem Intel-Beitrag. Der dort verlinkte Download `OpenVINO/Qwen3.8-27B-int4-ov` ist ein vor-konvertiertes OpenVINO-IR-Modell für `VLMPipeline`; seine Model Card nennt OpenVINO 2026.4.0 beziehungsweise GenAI-Nightly-Builds ab August 2026 als Kompatibilitätsvoraussetzung. Die aktuelle Esi.AI-Runtime verwendet OpenVINO und GenAI 2026.3.0. Für Qwen3.8 muss daher entweder die passende IR-Version mit einer kompatiblen Runtime eingesetzt oder eine Runtime mit expliziter `qwen35`-GGUF-Unterstützung verwendet werden.

Der vollständige IR-Bestand wurde unter `/home/llm/.cache/esi-ai/models/Qwen3.8-27B-int4-ov` geprüft. Das Verzeichnis enthält neben Sprachmodell, Tokenizer und Detokenizer auch `openvino_vision_embeddings_model.xml`, `openvino_vision_embeddings_pos_model.xml` und `openvino_vision_embeddings_merger_model.xml`. Esi.AI erkennt diese Vision-Komponente und verwendet dafür automatisch `VLMPipeline`; reine Text-IR-Verzeichnisse verwenden weiterhin `LLMPipeline`.

Für einen lokalen Ubuntu-26-Test wurden die OpenVINO-2026.4-Nightly-Bibliotheken aus den offiziellen Archiven geladen. Der GenAI-Build muss bei diesem Modell vom 14. August 2026 oder neuer sein:

```bash
runtime=/path/to/openvino_genai_ubuntu26_2026.4.0.0.dev20260814_x86_64/runtime
export OPENVINO_RUNTIME_DIR="$runtime/lib/intel64"
export OPENVINO_GENAI_C_LIBRARY="$runtime/lib/intel64/libopenvino_genai_c.so"
export LD_LIBRARY_PATH="$runtime/lib/intel64:$runtime/3rdparty/tbb/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
```

Die stabilen Esi.AI-NuGet-Referenzen bleiben bei 2026.3.0, da die geprüften 2026.4-Artefakte native Nightly-Tarballs und keine passenden stabilen NuGet-Pakete sind. Ein realer Test mit dem 14.-August-GenAI-Nightly, `GPU.1` und dem genannten Modell lud das Modell und erzeugte erfolgreich Text. Der Lauf mit dem 14.-Juli-GenAI-Nightly sowie der Lauf mit `LLMPipeline` wurden wegen der Modell-/Runtime-Inkompatibilität nicht als gültige Integration gewertet.

## Alternative: Hugging-Face-Modell nach OpenVINO IR exportieren

Für Architekturen außerhalb der direkten GGUF-Unterstützung wird das ursprüngliche Hugging-Face-Modell mit Optimum Intel exportiert:

```bash
python -m pip install "optimum-intel[openvino]"
optimum-cli export openvino \
  --model <owner>/<hugging-face-model> \
  --weight-format int4 \
  ./models/<model-name>-openvino
```

Das exportierte Verzeichnis enthält die OpenVINO-Modelldateien sowie die für GenAI benötigten Konfigurations- und Tokenizer-Dateien. Dieses Verzeichnis wird anschließend als `LLMPipeline`-Modellpfad verwendet.

## Relevanz für Esi.AI

Der aktuelle Esi.AI-Loader verwendet die passende GenAI-Abstraktion abhängig von der Modellstruktur:

```csharp
var pipeline = new VLMPipeline(modelPath, device); // bei vorhandener Vision-Komponente
```

Der Loader akzeptiert deshalb beide Formen:

- eine `.gguf`-Datei für den direkten GGUF-Weg;
- ein Verzeichnis für ein OpenVINO-IR-/GenAI-Modell; Vision-IR-Verzeichnisse werden als VLM geladen.

Die Validierung prüft für GGUF `File.Exists(modelPath)` und die Endung `.gguf`; für IR-Modelle bleibt `Directory.Exists(modelPath)` bestehen. Der gespeicherte Profilwert enthält den exakten GGUF-Dateipfad, nicht nur das übergeordnete Verzeichnis. Für den 2025.2-Kompatibilitätsweg muss zusätzlich ein Tokenizer-Verzeichnis konfigurierbar sein; bei neueren Runtimes kann dieses Feld entfallen.

Die bereits vorhandenen OpenVINO-Parameter bleiben davon getrennt:

- Modellpfad: GGUF-Dateipfad oder IR-Modellverzeichnis
- OpenVINO-Gerät: zum Beispiel `GPU.1`
- Generation: `max_new_tokens`, `temperature`, `top_p`, `do_sample`
- GPU-Routing: aktivierte kompatible Intel-Geräte und deren Prioritäten

## Einordnung von OpenVINO 2025.3

OpenVINO 2025.3 erweitert GenAI um zusätzliche LLM- und VLM-Familien, unter anderem Phi-4-mini-reasoning, AFM-4.5B und Gemma 3. Für NPU wurden Qwen3-Modelle ergänzt. Diese Modellunterstützung bezieht sich auf die jeweilige GenAI-/OpenVINO-Version und ist nicht automatisch eine Zusage für jede GGUF-Quantisierung.

Der Release-Beitrag beschreibt außerdem `TextRerankPipeline` für RAG sowie strukturierte Ausgabe über JSON-Schema, Regex oder EBNF-Grammatik. Diese Funktionen sind eigenständige GenAI-Funktionen und ändern den `LLMPipeline`-Pfad für Textgeneration nicht.

Für Esi.AI bedeutet das: Die Auswahl eines GGUF-Artefakts bleibt bewusst von der Laufzeitprüfung getrennt. Die UI kann lokale `.gguf`-Dateien anbieten, aber die Pipeline muss beim Laden weiterhin die konkrete Architektur, Quantisierung, GenAI-Version und das Zielgerät akzeptieren.

## Einordnung von OpenVINO 2026.3

Die aktuelle Dokumentation bestätigt weiterhin die direkte Übergabe einer `.gguf`-Datei an `LLMPipeline`, beschreibt diese Unterstützung aber als Preview mit begrenzter Topologieabdeckung. `SchedulerConfig` mit `cache_size` gehört zur Continuous-Batching- und Speculative-Decoding-Konfiguration und ist kein Feld der `GenerationConfig`. Die aktuelle Continuous-Batching-Konzeptseite ist noch als Work in Progress markiert.

Der lokale Esi.AI-C#-Wrapper exponiert `SchedulerConfig` noch nicht. Deshalb behandelt Esi.AI `CACHE_DIR` ausschließlich als persistenten Modellcache und behauptet keine konfigurierbare KV-Cache-Größe.

## OpenVINO GenAI auf NPU

Die OpenVINO-2026.3-NPU-Anleitung verwendet für LLMs primär nach OpenVINO IR exportierte Modelle und lädt sie mit `LLMPipeline(model_path, "NPU", pipeline_config)`. Für NPU-Modelle sind insbesondere diese Pipeline-Properties relevant:

- `MAX_PROMPT_LEN`: maximale Promptlänge, standardmäßig 1024 Tokens;
- `MIN_RESPONSE_LEN`: minimale Antwortlänge, standardmäßig 128 Tokens;
- `PREFILL_HINT`: `DYNAMIC` oder `STATIC` für die Promptverarbeitung;
- `GENERATE_HINT`: `FAST_COMPILE` oder `BEST_PERF` für Kompilierzeit versus Laufzeitleistung;
- `CACHE_DIR`: bevorzugter OpenVINO-Compile-Cache.

Esi.AI übernimmt diese Einstellungen nur für den aktiven `NPU`-Pfad. GPU und NPU werden nicht als gemischte `MULTI`-Route zusammengestellt. Die direkte GGUF-Unterstützung bleibt auch auf NPU von der jeweiligen Architektur, Quantisierung und Runtime abhängig; die NPU-Beispiele in der offiziellen Anleitung basieren auf exportierten IR-Modellen. Bei Fehlern ist daher ein Optimum-Intel-Export mit NPU-tauglicher INT4-/NF4-Konfiguration der verlässlichere Fallback. Für die NPU wird außerdem ein aktueller Intel-NPU-Treiber empfohlen.

## Quellen

- [OpenVINO GenAI: Inference with OpenVINO GenAI](https://docs.openvino.ai/2025/openvino-workflow-generative/inference-with-genai.html)
- [OpenVINO GenAI: Inference of GGUF models](https://docs.openvino.ai/2025/openvino-workflow-generative/inference-with-genai.html#inference-of-gguf-ggml-unified-format-models)
- [OpenVINO GenAI: Generative Model Preparation](https://docs.openvino.ai/2025/openvino-workflow-generative/genai-model-preparation.html)
- [OpenVINO 2026.3: Inference with OpenVINO GenAI](https://docs.openvino.ai/2026/openvino-workflow-generative/inference-with-genai.html)
- [OpenVINO GenAI: Continuous Batching](https://openvinotoolkit.github.io/openvino.genai/docs/concepts/optimization-techniques/continuous-batching)
- [OpenVINO 2026.3: OpenVINO GenAI on NPU](https://docs.openvino.ai/2026/openvino-workflow-generative/inference-with-genai/inference-with-genai-on-npu.html)
- [Intel OpenVINO Blog: OpenVINO GenAI Supports GGUF Models](https://blog.openvino.ai/blog-posts/openvino-genai-supports-gguf-models)
- [Intel OpenVINO Blog: GGUF Feature Update](https://blog.openvino.ai/blog-posts/openvino-genai-gguf-feature-update)
- [OpenVINO 2025.3: More GenAI, More Possibilities](https://medium.com/openvino-toolkit/openvino-2025-3-more-genai-more-possibilities-debb902fb718)
- [Hugging Face Hub CLI](https://huggingface.co/docs/huggingface_hub/en/guides/cli)

Die OpenVINO-Dokumentation weist darauf hin, dass die direkte GGUF-Unterstützung eine Preview-Funktion mit begrenzter Architekturabdeckung ist. Die genaue Unterstützung hängt von der installierten OpenVINO-GenAI-Version ab.
