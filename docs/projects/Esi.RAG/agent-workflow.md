# Agent workflow

`/api/ask` and the CLI `ask` command run a bounded investigation loop. Each round retrieves evidence, records a step, and stops at configured limits or when evidence is exhausted. Repository text is treated as untrusted data; it cannot issue commands, alter files, or override application instructions.
