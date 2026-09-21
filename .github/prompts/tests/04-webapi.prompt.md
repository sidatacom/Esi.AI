---
name: esi-ai-studio-test-webapi
description: Test the Esi.AI Studio Web API page and API configuration.
---

Test route `/webapi` in the running Esi.AI Studio browser session.

Checks:
1. Open `Web API` from navigation and verify no visible alert or unhandled exception.
2. Verify API status, base URL, port/configuration fields, authentication controls, and save/refresh actions are visible.
3. Change one approved non-destructive setting, save it, reload the page, and verify the persisted value.
4. Verify validation rejects an invalid value and presents a visible, actionable error without silently saving it.
5. If a loaded model is available, call the displayed OpenAI-compatible models endpoint and verify the response is valid.
6. If a loaded model is available, send one bounded chat-completions smoke request and verify success or an explicit backend error; verify no request hangs beyond the configured timeout.
7. Restore the original API settings and verify the final state.
8. If API or model prerequisites are unavailable, mark only those checks BLOCKED.

Do not expose secrets in the report. Report status, endpoint, response class (not tokens), visible evidence, and cleanup.
