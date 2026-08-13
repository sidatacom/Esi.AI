import * as http from "node:http";
import * as crypto from "node:crypto";
import { getServerSecret } from "./config.js";

export function isAllowedHttpRequest(request: http.IncomingMessage, secret = getServerSecret()): boolean {
  const host = request.headers.host ? (request.headers.host.startsWith("[") ? request.headers.host.slice(1, request.headers.host.indexOf("]")) : request.headers.host.split(":")[0]) : undefined;
  if (host && !["127.0.0.1", "localhost", "::1"].includes(host)) return false;
  if (request.headers.origin) {
    try { if (!["127.0.0.1", "localhost", "::1"].includes(new URL(request.headers.origin).hostname.replace(/^\[|\]$/g, ""))) return false; } catch { return false; }
  }
  if (!secret) return true;
  const authorization = request.headers.authorization;
  if (!authorization?.startsWith("Bearer ")) return false;
  const supplied = authorization.slice(7);
  return supplied.length === secret.length && crypto.timingSafeEqual(Buffer.from(supplied), Buffer.from(secret));
}
