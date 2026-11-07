const defaultMaxInputTokens = 32768;
const defaultMaxOutputTokens = 32768;

export interface ModelTokenLimitSource {
  context_length?: unknown;
  max_input_tokens?: unknown;
  max_output_tokens?: unknown;
}

export function resolveModelTokenLimits(
  model: ModelTokenLimitSource,
): { maxInputTokens: number; maxOutputTokens: number } {
  const contextLength = toPositiveInteger(model.context_length);
  const configuredOutputTokens = toPositiveInteger(model.max_output_tokens);
  const outputTokenLimit = configuredOutputTokens ?? defaultMaxOutputTokens;
  const maxOutputTokens =
    contextLength === undefined
      ? outputTokenLimit
      : Math.min(
          outputTokenLimit,
          Math.max(
            1,
            configuredOutputTokens === undefined
              ? Math.floor(contextLength / 2)
              : contextLength - 1,
          ),
        );
  const configuredInputTokens =
    toPositiveInteger(model.max_input_tokens) ?? defaultMaxInputTokens;
  const maxInputTokens =
    contextLength === undefined
      ? configuredInputTokens
      : Math.min(
          configuredInputTokens,
          Math.max(1, contextLength - maxOutputTokens),
        );

  return { maxInputTokens, maxOutputTokens };
}

function toPositiveInteger(value: unknown): number | undefined {
  return typeof value === "number" && Number.isSafeInteger(value) && value > 0
    ? value
    : undefined;
}
