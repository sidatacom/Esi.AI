---
name: esi-ai-studio-test-provider
description: Test the Esi.AI Studio Provider page and trace view.
---

Test route `/provider` in the running Esi.AI Studio browser session.

Checks:
1. Open `Provider` and verify no visible error alert or unhandled exception.
2. Verify provider status, base URL, model endpoint, chat endpoint, VS Code connection information, and trace panel are present.
3. Click `Status aktualisieren`; verify busy state and a completed result without an error.
4. When no model is loaded, verify the empty state and Backends action are correct.
5. When a model is loaded, verify it appears with runtime/backend information and that `Chat oeffnen` navigates to `/chats`.
6. Send one approved local provider request through the tested API path; return to `/provider` and verify a trace entry appears with direction, layer, timestamp, request id, and details.
7. Open a trace payload only if it contains no secret; verify the details element works.
8. Verify a failed request is represented as an actionable error and does not corrupt the provider page.

Do not claim provider readiness solely from page rendering. Report the exact state, route, trace evidence, and prerequisites.
