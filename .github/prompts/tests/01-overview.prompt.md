---
name: esi-ai-studio-test-overview
description: Test the Esi.AI Studio Overview page.
---

Test route `/` in the running Esi.AI Studio browser session.

Checks:
1. Open `/` through the Overview navigation item.
2. Verify the page renders without a visible `role=alert`, unhandled exception, or broken layout.
3. Verify the headings `Studio overview`, `Vom Client zum Modell`, `Modelle nach Runtime`, `Modelle nach Backend`, and `Geladene Modelle` are present.
4. Verify the status area is populated with active-model, runtime, backend, and API-status values.
5. Click `Status aktualisieren`; verify the button enters a busy state and returns to its normal state without an error.
6. Verify the navigation actions to Backends and Provider point to the correct routes.
7. Repeat the check with an empty runtime and record that state as valid, not as a failure.

Do not load or delete a model in this test. Report PASS, FAIL, or BLOCKED for every numbered check, evidence, and the final route URL. A missing prerequisite must be BLOCKED, never guessed as PASS.
