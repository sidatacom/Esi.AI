---
name: "Aggregate Monthly History"
description: "Merge each day's session logs into a monthly archive and create the month's thematic summary."
argument-hint: "Month as YYYY-MM, for example 2026-09"
agent: "agent"
---

Archive and summarize session logs for exactly one calendar month.

## Input and Targets

Expect the target month as `YYYY-MM`. If it is missing or ambiguous, ask before changing any files.

- Daily files: `docs/history/YYYY/MM/YYYYMMDD.md`, one file per day with existing session logs.
- Source files: after successful daily aggregation, move them to `docs/history/YYYY/MM/_source/`. Preserve their filenames and contents.
- Monthly summary: `docs/history/YYYY-<German-month-name>-summary.md`, for example `docs/history/2026-september-zusammenfassung.md`.

## Workflow

1. Find all raw session logs for the target month under `docs/history/`, including files in subdirectories and files with older names that lack timestamps. Use the session date established by the contents or filename. Exclude existing daily aggregates, monthly summaries, and this work log. Count the source files and days, and document ambiguous date assignments instead of guessing.
2. Read every discovered session log in full. For long files, read consecutive sections until you have covered the entire contents. Use search only to locate and navigate files, not as a substitute for reading them.
3. Create a daily file in the monthly directory for each day. Include the complete contents of every source file assigned to that day, clearly separated and headed by its original filename. Preserve text, statuses, dates, and code examples unchanged; do not shorten or summarize the daily sources.
4. Before moving files, verify that every source file is included exactly once and in full in the correct daily file. Then move the source files to `docs/history/YYYY/MM/_source/` without overwriting anything. If filenames collide, preserve the relative source path in a suitable subdirectory. Do not delete any source files.
5. Read `docs/history/2026-august-zusammenfassung.md` as the format reference. Create the monthly summary in German, with a title, the number of session logs and their date range, and one section per day. Within each day, group entries by topic rather than listing them by file. For each topic, include 2–5 bullets on work, decisions, and substantiated results, plus a compact list of source filenames. Conclude with 5–10 bullets covering the overall progression, important results, and open items. Match the number of daily sections to the actual source inventory.
6. Check the daily files, moved sources, and monthly summary for completeness, correct dates, and consistent counts. Read any existing target files first and preserve relevant content. Do not silently overwrite anything; resolve conflicts or potential data loss before making changes.

Use only statements substantiated by the session logs. Distinguish completed work from investigations, proposals, failures, and open items. When statuses differ, cite the relevant dates and evidence rather than claiming an unsupported resolution.

At the end, report the monthly directory, the number of daily files and source files, the path to the monthly summary, and any unassigned or conflicting files. Do not claim successful completion if a completeness check has failed.
