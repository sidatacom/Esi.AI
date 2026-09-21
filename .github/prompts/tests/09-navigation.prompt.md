---
name: esi-ai-studio-test-navigation
description: Test the complete Esi.AI Studio navigation shell and fallback behavior.
---

Test the shared navigation in this exact order:
`/` -> `/backends` -> `/models` -> `/webapi` -> `/chats` -> `/provider` -> `/settings` -> `/auth`.

Checks:
1. From each route, click the corresponding navigation item instead of using only direct URLs.
2. Verify the URL, active navigation item, page heading, and main content change correctly.
3. Verify the shell remains usable after every transition and no visible alert or unhandled exception appears.
4. Verify the navigation shell is visibly laid out as a sidebar: `#studio-navigation` is visible, links are stacked vertically, each link is a visible block spanning the navigation width, and links do not use default underlines.
5. Verify the navigation contains the expected visible labels in order: `Overview`, `Backends`, `Models`, `Web API`, `Chats`, `Provider`, `Settings`, `Auth required`, and the appropriate account actions.
6. Verify the current route has exactly one active navigation link with `aria-current="page"`, a visibly distinct active style, and readable contrast.
7. Verify the shell remains usable at a narrow viewport: the navigation does not overflow horizontally, labels remain readable, and the active link remains visible.
8. Verify browser back and forward preserve the expected route and page state.
9. Verify refresh on every route returns to the same route.
10. Verify an unknown route renders the Not Found page and does not crash the shell.
11. Verify the brand link returns to `/`.
12. Record any route that is intentionally blocked by authentication or missing runtime prerequisites.

Report one result per route plus a final shell result. A transient startup connection failure must be retried after host readiness before it is classified as a failure.
