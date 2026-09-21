---
name: esi-ai-studio-test-chats
description: Test the Esi.AI Studio Chats page and conversation lifecycle.
---

Test route `/chats` in the running Esi.AI Studio browser session.

Checks:
1. Open `Chats` and verify the archive, new-chat action, conversation area, and model selector render without alerts.
2. Create one test chat and verify it appears in the archive with a stable title.
3. Select an available loaded model and verify the selection is retained for the current chat.
4. Send one short deterministic prompt; verify sending state, assistant response or explicit backend error, and completion without hanging.
5. Verify the user message and assistant result are displayed in the correct order after a reload.
6. Create a second test chat, switch between both chats, and verify their histories remain separate.
7. Delete or reset only the chats created by this test if the UI supports it; otherwise record the cleanup limitation.
8. If no loaded model exists, run navigation/rendering checks and mark model-dependent checks BLOCKED.

Use no sensitive content. Report PASS, FAIL, or BLOCKED per check, include response timing class and cleanup status.
