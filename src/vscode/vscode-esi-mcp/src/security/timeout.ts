/**
 * Creates a timeout wrapper for async operations.
 * Returns a promise that resolves with the operation result or
 * rejects with a timeout error.
 */
export function withTimeout<T>(
  operation: Promise<T>,
  timeoutMs: number,
  onTimeout?: () => void,
): Promise<{ result: T; timedOut: false } | { result: undefined; timedOut: true }> {
  return new Promise((resolve) => {
    let resolved = false;

    const timer = setTimeout(() => {
      if (resolved) return;
      resolved = true;
      onTimeout?.();
      resolve({ result: undefined, timedOut: true });
    }, timeoutMs);

    operation
      .then((result) => {
        if (resolved) return;
        resolved = true;
        clearTimeout(timer);
        resolve({ result, timedOut: false });
      })
      .catch(() => {
        if (resolved) return;
        resolved = true;
        clearTimeout(timer);
        resolve({ result: undefined, timedOut: true });
      });
  });
}

/**
 * Clamp a timeout value within allowed bounds.
 */
export function clampTimeout(
  requestedMs: number | undefined,
  defaultMs: number,
  maxMs: number = 300000,
): number {
  const value = requestedMs ?? defaultMs;
  return Math.max(1000, Math.min(value, maxMs));
}
