---
name: esi-ai-studio-test-models
description: Test the Esi.AI Studio Models library, search, filters, and downloads.
---

Test route `/models` in the running Esi.AI Studio browser session.

Checks:
1. Open `Models` and verify the page renders without visible alerts or unhandled exceptions.
2. Verify the `Meine Model Library` tab shows local model counts, configured directories, supported formats, compatibility controls, provider capabilities, and model actions.
3. Toggle a compatibility or provider-capability option for an approved test model; reload the page and verify persistence.
4. Open `Hugging Face suchen`; verify search input, search action, filter groups, hardware-fit controls, context length, sort order, and clear-filters behavior.
5. Run one search using a deterministic test query only when network access is approved; verify loading, result, empty-result, and error handling as applicable.
6. For an approved small test artifact, verify download target/options, queued/progress/completed state, pause/resume, and failed-download display.
7. Delete only test downloads and verify the collection is reconciled; do not delete existing user models.
8. Verify the model delete dialog supports cancel and that cancellation leaves the model unchanged.
9. If no local model, network permission, or test artifact exists, mark only dependent checks BLOCKED.

Report PASS, FAIL, or BLOCKED per check, include route and visible evidence, and list every created artifact and cleanup action.
