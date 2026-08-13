import { describe, it, expect } from "vitest";
import { CommandGuard } from "../../src/security/command-guard.js";
import type { SecurityConfig } from "../../src/types/index.js";

function createConfig(overrides: Partial<SecurityConfig> = {}): SecurityConfig {
  return {
    blockedCommands: ["rm -rf /", "mkfs", "dd if=", ":(){ :|:& };:"],
    allowedDirectories: [],
    defaultTimeoutMs: 30000,
    maxConcurrentSessions: 10,
    includeAllTerminals: false,
    maxOutputLines: 10000,
    idleTimeoutMs: 300000,
    ...overrides,
  };
}

describe("CommandGuard", () => {
  describe("validateCommand", () => {
    it("should allow safe commands", () => {
      const guard = new CommandGuard(createConfig());
      expect(guard.validateCommand("ls -la").valid).toBe(true);
      expect(guard.validateCommand("npm test").valid).toBe(true);
      expect(guard.validateCommand("git status").valid).toBe(true);
    });

    it("should block dangerous commands", () => {
      const guard = new CommandGuard(createConfig());

      const result = guard.validateCommand("rm -rf /");
      expect(result.valid).toBe(false);
      expect(result.reason).toContain("rm -rf /");
    });

    it("should block fork bomb", () => {
      const guard = new CommandGuard(createConfig());
      const result = guard.validateCommand(":(){ :|:& };:");
      expect(result.valid).toBe(false);
    });

    it("should block dd command", () => {
      const guard = new CommandGuard(createConfig());
      const result = guard.validateCommand("dd if=/dev/zero of=/dev/sda");
      expect(result.valid).toBe(false);
      expect(result.reason).toContain("dd if=");
    });

    it("should reject empty commands", () => {
      const guard = new CommandGuard(createConfig());
      expect(guard.validateCommand("").valid).toBe(false);
      expect(guard.validateCommand("   ").valid).toBe(false);
    });

    it("should enforce allowlist when configured", () => {
      const guard = new CommandGuard(
        createConfig({
          allowedCommands: ["npm", "node", "git"],
        }),
      );

      expect(guard.validateCommand("npm test").valid).toBe(true);
      expect(guard.validateCommand("node index.js").valid).toBe(true);
      expect(guard.validateCommand("python script.py").valid).toBe(false);
    });

    it("should update config dynamically", () => {
      const guard = new CommandGuard(createConfig());
      expect(guard.validateCommand("curl http://example.com").valid).toBe(true);

      guard.updateConfig(
        createConfig({ blockedCommands: ["curl"] }),
      );
      expect(guard.validateCommand("curl http://example.com").valid).toBe(
        false,
      );
    });
  });

  describe("validateDirectory", () => {
    it("should allow any directory when no restrictions", () => {
      const guard = new CommandGuard(createConfig());
      expect(guard.validateDirectory("/tmp").valid).toBe(true);
      expect(guard.validateDirectory("/home/user").valid).toBe(true);
    });

    it("should restrict to allowed directories", () => {
      const guard = new CommandGuard(
        createConfig({
          allowedDirectories: ["/home/user/projects"],
        }),
      );

      expect(
        guard.validateDirectory("/home/user/projects/myapp").valid,
      ).toBe(true);
      expect(guard.validateDirectory("/tmp").valid).toBe(false);
      expect(guard.validateDirectory("/home/user").valid).toBe(false);
    });

    it("should resolve relative paths", () => {
      const guard = new CommandGuard(
        createConfig({
          allowedDirectories: ["/home/user/projects"],
        }),
      );

      // Relative paths get resolved against process.cwd()
      const result = guard.validateDirectory("/home/user/projects/./subdir");
      expect(result.valid).toBe(true);
    });
  });
});
