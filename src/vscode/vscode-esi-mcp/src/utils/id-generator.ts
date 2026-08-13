import { v4 as uuidv4 } from "uuid";

export function generateSessionId(): string {
  return `session-${uuidv4().slice(0, 8)}`;
}

export function generateCommandId(): string {
  return `cmd-${uuidv4().slice(0, 8)}`;
}
