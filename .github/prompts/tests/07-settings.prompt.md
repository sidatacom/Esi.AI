---
name: esi-ai-studio-test-settings
description: Test the Esi.AI Studio Settings page and persistence.
---

Test route `/settings` in the running Esi.AI Studio browser session.

Checks:
1. Open `Settings` and verify the form renders without visible alerts or unhandled exceptions.
2. Verify every displayed runtime setting has a label, input/control, current value, and save action.
3. Record the original values, change one approved value, save, and verify success status.
4. Reload the page and verify the changed value persists.
5. Enter an invalid boundary value; verify client/server validation rejects it with a visible error and does not persist it.
6. Restore the original values, save, reload, and verify restoration.
7. Verify save busy state prevents duplicate submissions and returns to idle.

Do not change security or production-sensitive settings without explicit approval. Report values only when they are non-secret; redact tokens and credentials. Mark unavailable controls BLOCKED.
