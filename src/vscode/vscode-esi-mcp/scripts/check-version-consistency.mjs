import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const extensionDirectory = dirname(dirname(fileURLToPath(import.meta.url)));
const packageJson = JSON.parse(readFileSync(join(extensionDirectory, "package.json"), "utf8"));
const changelog = readFileSync(join(extensionDirectory, "CHANGELOG.md"), "utf8");
const currentVersion = packageJson.version;
const versionPattern = "\\d+\\.\\d+\\.\\d+";
const errors = [];

function parseVersion(version) {
  const match = new RegExp(`^(?<version>${versionPattern})$`).exec(version);
  return match ? match.groups.version.split(".").map(Number) : null;
}

function compareVersions(left, right) {
  for (let index = 0; index < left.length; index += 1) {
    if (left[index] !== right[index]) return left[index] - right[index];
  }
  return 0;
}

function readSourceVersion(relativePath, pattern) {
  const source = readFileSync(join(extensionDirectory, relativePath), "utf8");
  return source.match(pattern)?.[1];
}

const parsedCurrentVersion = parseVersion(currentVersion);
if (!parsedCurrentVersion) {
  errors.push(`package.json version "${currentVersion}" is not a stable three-part release version.`);
}

const serverVersion = readSourceVersion("src/mcp-http-server.ts", /version:\s*"([^"]+)"/);
const handshakeVersion = readSourceVersion("src/mcp/server.ts", /version:\s*"([^"]+)"/);
if (serverVersion !== currentVersion) {
  errors.push(`src/mcp-http-server.ts reports ${serverVersion ?? "no version"}, expected ${currentVersion}.`);
}
if (handshakeVersion !== currentVersion) {
  errors.push(`src/mcp/server.ts reports ${handshakeVersion ?? "no version"}, expected ${currentVersion}.`);
}

const changelogVersion = changelog.match(/^## \[(\d+\.\d+\.\d+)\]/m)?.[1];
if (changelogVersion !== currentVersion) {
  errors.push(`CHANGELOG.md latest release is ${changelogVersion ?? "missing"}, expected ${currentVersion}.`);
}

try {
  const repositoryRoot = execFileSync("git", ["rev-parse", "--show-toplevel"], {
    cwd: extensionDirectory,
    encoding: "utf8",
  }).trim();
  const repositoryExtensionPath = relative(repositoryRoot, extensionDirectory).split(sep).join("/");
  const basePackage = JSON.parse(execFileSync("git", ["show", `HEAD:${repositoryExtensionPath}/package.json`], {
    cwd: extensionDirectory,
    encoding: "utf8",
  }));
  const baseVersion = parseVersion(basePackage.version);
  const changedFiles = execFileSync("git", ["status", "--short", "--untracked-files=all", "--", repositoryExtensionPath], {
    cwd: repositoryRoot,
    encoding: "utf8",
  })
    .trim()
    .split(/\r?\n/)
    .filter(Boolean)
    .map((file) => file.slice(3));

  if (!baseVersion) {
    errors.push(`HEAD package.json version "${basePackage.version}" is not a stable three-part release version.`);
  } else if (changedFiles.length > 0 && (!parsedCurrentVersion || compareVersions(parsedCurrentVersion, baseVersion) <= 0)) {
    errors.push(
      `Extension files changed (${changedFiles.join(", ")}) but version ${currentVersion} is not higher than HEAD version ${basePackage.version}; choose and synchronize a new release version before building or packaging.`,
    );
  }
} catch (error) {
  errors.push(`Could not compare the release version with HEAD: ${error.message}`);
}

if (errors.length > 0) {
  console.error("EsiMCP version consistency check failed:");
  for (const error of errors) console.error(`- ${error}`);
  process.exitCode = 1;
} else {
  console.log(`EsiMCP version consistency check passed for ${currentVersion}.`);
}