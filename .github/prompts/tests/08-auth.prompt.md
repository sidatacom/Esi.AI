---
name: esi-ai-studio-test-auth
description: Test Auth required and account navigation without destructive account changes.
---

Test routes `/auth`, `/Account/Login`, `/Account/Register`, and `/Account/Manage` only when the required authentication fixture is provided.

Checks:
1. Open `Auth required` and verify the protected-page explanation and navigation render without alerts.
2. As an unauthenticated user, verify protected navigation does not expose unauthorized application actions.
3. Open `Account/Login`; verify required fields, validation, submit state, and actionable invalid-credentials feedback.
4. Open `Account/Register` only with a disposable test identity; verify required-field and invalid-input validation.
5. Complete registration/login only with explicit test credentials supplied through the secure test environment, never in the report.
6. Verify the authenticated navigation shows the user entry and `Log out` action.
7. Verify `Account/Manage` is reachable only when authenticated and that logout returns to the expected unauthenticated state.
8. If no auth fixture or disposable identity is available, mark credential-dependent checks BLOCKED, not PASS.

Never print passwords, tokens, cookies, or personal data. Report only route, authentication state class, visible validation, and cleanup result.
