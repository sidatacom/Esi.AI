#!/usr/bin/env node

import { performance } from "node:perf_hooks";

const options = parseArguments(process.argv.slice(2));
const baseUrl = (options["base-url"] ?? process.env.ESI_STUDIO_URL ?? "http://localhost:7010").replace(/\/$/, "");
const model = options.model;
const repeatCount = parsePositiveInteger(options["repeat-count"], "--repeat-count");
const warmups = parseNonNegativeInteger(options.warmups ?? "1", "--warmups");
const runs = parsePositiveInteger(options.runs ?? "5", "--runs");

if (!model) {
    throw new Error("Pass the Studio model configuration ID with --model.");
}

const results = [];
const totalRuns = warmups + runs;
for (let index = 0; index < totalRuns; index++) {
    const kind = index < warmups ? "warmup" : "measured";
    const runId = `QWEN38-BENCH-V1-${String(index + 1).padStart(2, "0")}`;
    const result = await runRequest({ baseUrl, model, repeatCount, runId, kind });
    results.push(result);
    process.stdout.write(`${JSON.stringify(result)}\n`);
}

const measured = results.filter(result => result.kind === "measured");
process.stdout.write(`${JSON.stringify({
    summary: {
        baseUrl,
        model,
        repeatCount,
        warmups,
        measuredRuns: runs,
        medianClientDecodeTokensPerSecond: median(measured.map(result => result.clientDecodeTokensPerSecond)),
        medianServerTokensPerSecond: median(measured.map(result => result.serverTokensPerSecond)),
        allMeasuredRunsReachedTokenLimit: measured.every(result => result.completionLengthMatchesLimit)
    }
}, null, 2)}\n`);

async function runRequest({ baseUrl, model, repeatCount, runId, kind }) {
    const prompt = [
        "ESI-QWEN38-AGENT-BENCH-V1",
        "You are GitHub Copilot, an autonomous coding agent working in a VS Code repository.",
        `Run: ${runId}`,
        "Repository context padding (synthetic token-fill; no source files or tools are supplied):",
        "hello ".repeat(repeatCount),
        "Agent request:",
        "Investigate a streaming-cancellation bug in a C# service: cancelling a client request must stop generation, release backend resources, and preserve caller cancellation semantics. Trace the owning code path, propose the smallest root-cause fix, and include a focused regression test. Do not invent APIs or claim that tools were run. Continue with concrete patch and test details until the response token limit; do not end with an early summary."
    ].join("\n");
    const requestStart = performance.now();
    const response = await fetch(`${baseUrl}/v1/chat/completions`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            model,
            messages: [{ role: "user", content: prompt }],
            max_tokens: 128,
            temperature: 0,
            top_p: 1,
            top_k: 1,
            min_p: 0,
            repetition_penalty: 1,
            frequency_penalty: 0,
            presence_penalty: 0,
            seed: 42,
            reasoning_effort: "none",
            stream: true,
            stream_options: { include_usage: true }
        }),
        signal: AbortSignal.timeout(600_000)
    });

    if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${await response.text()}`);
    }
    if (!response.body) {
        throw new Error("The Studio response did not contain an SSE stream.");
    }

    let usage = null;
    let finishReason = null;
    let firstContentAt = null;
    let lastContentAt = null;
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";

    while (true) {
        const { done, value } = await reader.read();
        buffer += decoder.decode(value ?? new Uint8Array(), { stream: !done });
        const lines = buffer.split("\n");
        buffer = lines.pop() ?? "";
        for (const line of lines) {
            ({ usage, finishReason, firstContentAt, lastContentAt } = consumeSseLine(line, {
                usage, finishReason, firstContentAt, lastContentAt
            }));
        }
        if (done)
            break;
    }
    if (buffer.length > 0) {
        ({ usage, finishReason, firstContentAt, lastContentAt } = consumeSseLine(buffer, {
            usage, finishReason, firstContentAt, lastContentAt
        }));
    }

    const completedAt = performance.now();
    if (!usage || !Number.isFinite(usage.completion_tokens)) {
        throw new Error("The final SSE usage chunk is missing; enable stream_options.include_usage.");
    }

    const decodeDurationMs = firstContentAt !== null && lastContentAt !== null
        ? Math.max(0, lastContentAt - firstContentAt)
        : null;
    const completionTokens = usage.completion_tokens;
    return {
        kind,
        runId,
        httpStatus: response.status,
        promptTokens: usage.prompt_tokens ?? null,
        completionTokens,
        finishReason,
        clientTimeToFirstTokenMs: firstContentAt === null ? null : round(firstContentAt - requestStart),
        clientDecodeDurationMs: decodeDurationMs === null ? null : round(decodeDurationMs),
        clientDecodeTokensPerSecond: decodeDurationMs > 0 && completionTokens > 1
            ? round((completionTokens - 1) * 1000 / decodeDurationMs)
            : null,
        clientWallTimeMs: round(completedAt - requestStart),
        serverTokensPerSecond: usage.tokens_per_second ?? null,
        completionLengthMatchesLimit: completionTokens === 128 && finishReason === "length",
        fits131072Context: Number.isFinite(usage.prompt_tokens) && usage.prompt_tokens + completionTokens <= 131072
    };
}

function consumeSseLine(line, state) {
    const normalized = line.replace(/\r$/, "");
    if (!normalized.startsWith("data:"))
        return state;

    const payload = normalized.slice(5).trim();
    if (!payload || payload === "[DONE]")
        return state;

    const chunk = JSON.parse(payload);
    const usage = chunk.usage ?? state.usage;
    const choice = chunk.choices?.[0];
    const content = choice?.delta?.content;
    let firstContentAt = state.firstContentAt;
    let lastContentAt = state.lastContentAt;
    if (typeof content === "string" && content.length > 0) {
        const now = performance.now();
        firstContentAt ??= now;
        lastContentAt = now;
    }

    return {
        usage,
        finishReason: choice?.finish_reason ?? state.finishReason,
        firstContentAt,
        lastContentAt
    };
}

function parseArguments(args) {
    const parsed = {};
    for (let index = 0; index < args.length; index++) {
        const key = args[index];
        if (!key.startsWith("--") || index + 1 >= args.length)
            throw new Error(`Expected --option value, received '${key}'.`);
        parsed[key.slice(2)] = args[++index];
    }
    return parsed;
}

function parsePositiveInteger(value, name) {
    const parsed = Number(value);
    if (!Number.isSafeInteger(parsed) || parsed < 1)
        throw new Error(`${name} must be a positive integer.`);
    return parsed;
}

function parseNonNegativeInteger(value, name) {
    const parsed = Number(value);
    if (!Number.isSafeInteger(parsed) || parsed < 0)
        throw new Error(`${name} must be a non-negative integer.`);
    return parsed;
}

function median(values) {
    const sorted = values.filter(Number.isFinite).sort((left, right) => left - right);
    if (sorted.length === 0)
        return null;
    const middle = Math.floor(sorted.length / 2);
    return round(sorted.length % 2 === 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle]);
}

function round(value) {
    return Math.round(value * 100) / 100;
}