export const DEFAULT_SERVER_PORT = 3002;
export const DEFAULT_BIND_HOSTS = ["127.0.0.1", "::1"] as const;

export function normalizePort(value: unknown, fallback = DEFAULT_SERVER_PORT): number {
  const port = typeof value === "number" ? value : Number(value);
  return Number.isInteger(port) && port >= 1 && port <= 65535 ? port : fallback;
}

export function getServerPort(environment: NodeJS.ProcessEnv = process.env): number {
  return normalizePort(environment.ESIMCP_SERVER_PORT);
}

export function normalizeBindHosts(value: unknown): string[] {
  const hosts = Array.isArray(value) ? value : typeof value === "string" ? [value] : [...DEFAULT_BIND_HOSTS];
  const normalized = [...new Set(hosts.filter((host): host is string => typeof host === "string" && host.trim().length > 0).map((host) => host.trim()))];
  if (normalized.length === 0) return [...DEFAULT_BIND_HOSTS];
  const invalid = normalized.find((host) => !DEFAULT_BIND_HOSTS.includes(host as typeof DEFAULT_BIND_HOSTS[number]));
  if (invalid) throw new Error(`esimcp.bindHost must contain only loopback hosts; received ${invalid}`);
  return normalized;
}

export function getServerSecret(environment: NodeJS.ProcessEnv = process.env): string | undefined {
  return environment.ESIMCP_SECRET || undefined;

}