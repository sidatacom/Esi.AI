const fs = require("node:fs/promises");
const fsSync = require("node:fs");
const net = require("node:net");
const os = require("node:os");
const path = require("node:path");
const { runTests } = require("@vscode/test-electron");

async function findFreePort() {
  const server = net.createServer();
  await new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", resolve);
  });
  const address = server.address();
  await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
  if (!address || typeof address === "string") throw new Error("Could not allocate a local test port");
  return address.port;
}

async function main() {
  const extensionRoot = path.resolve(__dirname, "../..");
  const workspaceRoot = await fs.mkdtemp(path.join(os.tmpdir(), "esimcp-debug-integration-"));
  const port = await findFreePort();
  const workspaceConfig = path.join(workspaceRoot, ".vscode");
  await fs.mkdir(workspaceConfig, { recursive: true });
  await fs.writeFile(path.join(workspaceConfig, "settings.json"), JSON.stringify({
    "esimcp.serverPort": port,
    "esimcp.debugStateTimeoutMs": 10000,
    "esimcp.debugAdapterTimeoutMs": 5000,
    "esimcp.debugHostReadinessTimeoutSeconds": 5,
  }, null, 2));
  await fs.writeFile(path.join(workspaceRoot, "debug-target.cjs"), [
    "function integrationTarget() {",
    '  const localValue = 41;',
    '  const accessToken = "integration-secret-token";',
    "  debugger;",
    "  const incrementedValue = localValue + 1;",
    "  setInterval(() => void (incrementedValue + accessToken.length), 1000);",
    "}",
    "",
    "integrationTarget();",
    "",
  ].join("\n"));
  const watchPort = await findFreePort();
  const watchProject = path.join(workspaceRoot, "WatchFixture.csproj");
  const watchProgram = (value) => [
    "var app = WebApplication.Create();",
    "app.MapGet(\"/\", () => WatchVersion.Current());",
    "app.MapGet(\"/pid\", () => Environment.ProcessId);",
    `await app.RunAsync(\"http://127.0.0.1:${watchPort}\");`,
    "",
    "static class WatchVersion",
    "{",
    `    public static string Current() => \"${value}\";`,
    "}",
    "",
  ].join("\n");
  await fs.writeFile(watchProject, [
    "<Project Sdk=\"Microsoft.NET.Sdk.Web\">",
    "  <PropertyGroup>",
    "    <TargetFramework>net10.0</TargetFramework>",
    "    <ImplicitUsings>enable</ImplicitUsings>",
    "    <Nullable>enable</Nullable>",
    "  </PropertyGroup>",
    "</Project>",
    "",
  ].join("\n"));
  await fs.writeFile(path.join(workspaceRoot, "Program.cs"), watchProgram("watch-before"));
  await fs.writeFile(path.join(workspaceConfig, "launch.json"), JSON.stringify({
    version: "0.2.0",
    configurations: [{
      name: "EsiMCP Integration",
      type: "node",
      request: "launch",
      program: "${workspaceFolder}/debug-target.cjs",
      cwd: "${workspaceFolder}",
      console: "internalConsole",
      preLaunchTask: "integration-build",
    }],
  }, null, 2));
  await fs.writeFile(path.join(workspaceConfig, "tasks.json"), JSON.stringify({
    version: "2.0.0",
    tasks: [{ label: "integration-build", type: "process", command: process.execPath, args: ["-e", "process.exit(0)"], problemMatcher: [] }],
  }, null, 2));

  const localCode = process.env.VSCODE_EXECUTABLE_PATH
    ?? ["/snap/code/current/usr/share/code/code", "/usr/share/code/code"].find((candidate) => fsSync.existsSync(candidate));
  const options = {
    extensionDevelopmentPath: [extensionRoot],
    extensionTestsPath: path.join(__dirname, "suite", "index.cjs"),
    extensionTestsEnv: { ESIMCP_TEST_PORT: String(port), ESIMCP_WATCH_PORT: String(watchPort) },
    launchArgs: [workspaceRoot, "--disable-gpu", "--disable-dev-shm-usage", "--ozone-platform=x11"],
  };
  if (localCode) options.vscodeExecutablePath = localCode;
  else options.version = process.env.VSCODE_TEST_VERSION ?? "1.99.0";

  try {
    const exitCode = await runTests(options);
    if (exitCode !== 0) process.exitCode = exitCode;
  } finally {
    await fs.rm(workspaceRoot, { recursive: true, force: true });
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
