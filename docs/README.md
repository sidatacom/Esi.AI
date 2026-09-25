# Esi.AI Dokumentation

Dieser Index fuehrt durch die aktive Produkt- und Entwicklungsdokumentation. Sitzungsnotizen und Arbeitsprotokolle liegen getrennt unter [`history/`](history/); sie sind kein Ersatz fuer gepflegte Produktdokumentation.

## Studio und Architektur

- [Layer-Design: Backend, Flow und Request](projects/Esi.AI/studio-layers-design.md): vorgeschlagene Informationsarchitektur, Agent-Bausteine, Editor-Layout und Abgrenzung zum aktuellen Funktionsstand.
- [Studio-Entwicklung](projects/Esi.AI/development/studio-development.md): Starten, Debugging und Hot Reload.
- [EsiMCP Debug-Lifecycle](projects/Esi.AI/development/esimcp-debug-lifecycle.md): VS-Code-Debug-Lifecycle und Diagnose.
- [OpenAI-kompatible WebAPI und VS-Code-Provider](projects/Esi.AI/OpenAI-Compatible-WebAPI-and-Model-Provider.md): Request-Vertrag, Streaming und Provider-Verhalten.

## Backends und Modelle

- [Backend Assemblies](projects/Esi.AI/backends/backend-assemblies.md): Package-Grenzen und Migrationsstand.
- [Backend Runtime Gallery](projects/Esi.AI/backends/backend-runtime-gallery.md): Runtime-Quellen, Installation und Voraussetzungen.
- [Structured Tool Calling](projects/Esi.AI/backends/backend-tool-calling.md): backend-spezifische Tool-Unterstuetzung.
- [OpenVINO GenAI und GGUF](projects/Esi.AI/backends/openvino-genai-gguf.md): OpenVINO-Modelle und GGUF-Hinweise.
- [Reference Models](projects/Esi.AI/models/reference-models.md): Referenzmodelle fuer Smoke- und Integrationstests.
- [vLLM gRPC](projects/Esi.AI/backends/vllm-grpc.md): Python-Runtime und gRPC-Vertrag.

## Projekte

- [Esi.AI Projektuebersicht](projects/Esi.AI/README.md)
- [Esi.AI.Core](projects/Esi.AI/Esi.AI.Core/README.md)
- [Esi.AI.Studio](projects/Esi.AI/Esi.AI.Studio/README.md)
- [Esi.AI.Studio.Client](projects/Esi.AI/Esi.AI.Studio.Client/README.md)
- [Esi.RAG Architektur](projects/Esi.RAG/ARCHITECTURE.md)
- [Esi.RAG Agent Workflow](projects/Esi.RAG/agent-workflow.md)
- [Esi.RAG Ingestion](projects/Esi.RAG/ingestion.md)
- [Esi.RAG Retrieval](projects/Esi.RAG/retrieval.md)
- [Esi.RAG Sicherheit](projects/Esi.RAG/security.md)
- [Esi.RAG Mandantendaten](projects/Esi.RAG/tenant-data.md)
- [Esi.RAG Nutzung](projects/Esi.RAG/usage.md)

## Benchmark

- [Benchmark-Index](projects/Esi.AI/benchmarks/README.md): Testaufbau, Ergebnisse und Verweise auf die einzelnen Benchmarklaeufe.

## Weitere Inhalte

- [Esi.AI News](projects/Esi.AI/news/): zeitgebundene Projektankuendigungen.
- [Dokumentationsbilder](projects/Esi.AI/assets/images/): Studio- und Backend-Abbildungen.

Benchmark-Einzelberichte sind ueber den Benchmark-Index erreichbar. Die chronologische Ablage in `history/` bleibt bewusst ausserhalb der Themenstruktur.