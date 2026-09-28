const assert = require("node:assert/strict");
const http = require("node:http");
const path = require("node:path");
const vscode = require("vscode");

const port = Number(process.env.ESIMCP_TEST_PORT);
let mcpSessionId;
let requestId = 0;

async function rpc(method, params) {
  const headers = {
    accept: "application/json, text/event-stream",
    "content-type": "application/json",
  };
  if (mcpSessionId) headers["mcp-session-id"] = mcpSessionId;
  const response = await fetch(`http://127.0.0.1:${port}/mcp`, {
    method: "POST",
    headers,
    body: JSON.stringify({ jsonrpc: "2.0", id: ++requestId, method, params }),
  });
  const receivedSessionId = response.headers.get("mcp-session-id");
  if (receivedSessionId) mcpSessionId = receivedSessionId;
  const body = await response.text();
  assert.equal(response.ok, true, `MCP ${method} returned HTTP ${response.status}: ${body}`);
  const dataLine = body.split(/\r?\n/).find((line) => line.startsWith("data: "));
  assert.ok(dataLine, `MCP ${method} returned no SSE data: ${body}`);
  const message = JSON.parse(dataLine.slice(6));
  if (message.error) throw new Error(`MCP ${method} failed: ${message.error.message}`);
  return message.result;
}

function parseToolResult(result) {
  if (result?.isError) throw new Error(result.content?.map((item) => item.text).join("\n") ?? "MCP tool failed");
  const text = result?.content?.find((item) => item.type === "text")?.text;
  assert.equal(typeof text, "string", "MCP tool returned no text content");
  return JSON.parse(text);
}

async function callTool(name, args = {}) {
  return rpc("tools/call", { name, arguments: args });
}

async function debug(commandId, args = {}) {
  return parseToolResult(await callTool("vscode_debug_execute_command", { commandId, arguments: args }));
}

async function waitFor(predicate, description, timeoutMs = 10000) {
  const deadline = Date.now() + timeoutMs;
  while (!predicate()) {
    if (Date.now() >= deadline) throw new Error(`Timed out waiting for ${description}`);
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
}

async function waitForHttpText(url, expected, description, timeoutMs = 90000) {
  const deadline = Date.now() + timeoutMs;
  let lastValue;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url);
      if (response.ok) {
        lastValue = await response.text();
        if (lastValue === expected) return lastValue;
      }
    } catch {
      // The app may still be building or applying the edit.
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  throw new Error(`Timed out waiting for ${description}; last value was ${lastValue ?? "unavailable"}`);
}

async function run() {
  assert.ok(Number.isInteger(port) && port > 0, "Missing EsiMCP integration test port");
  const esimcp = vscode.extensions.getExtension("sidatacom.vscode-esi-mcp");
  assert.ok(esimcp, "EsiMCP development extension was not loaded");
  await esimcp.activate();

  let readinessServer;
  try {
    let initialized;
    const initializeDeadline = Date.now() + 20000;
    while (!initialized) {
      try {
        initialized = await rpc("initialize", {
          protocolVersion: "2024-11-05",
          capabilities: {},
          clientInfo: { name: "EsiMCP debug integration tests", version: "1.0.0" },
        });
      } catch (error) {
        if (Date.now() >= initializeDeadline) throw error;
        await new Promise((resolve) => setTimeout(resolve, 100));
      }
    }

    const listedTools = await rpc("tools/list", {});
    const toolNames = new Set(listedTools.tools.map((tool) => tool.name));
    for (const name of [
      "vscode_terminal_list_commands",
      "vscode_terminal_execute_command",
      "vscode_debug_list_commands",
      "vscode_debug_execute_command",
      "msaccess_list_commands",
      "msaccess_execute_command",
    ]) {
      assert.ok(toolNames.has(name), `EsiMCP did not register ${name}`);
    }
    assert.equal([...toolNames].some((name) => name.startsWith("csharp_devkit_")), false);
    const debugCatalog = parseToolResult(await callTool("vscode_debug_list_commands"));
    assert.ok(debugCatalog.commands.some((command) => command.command === "debug.start"));
    assert.ok(debugCatalog.commands.some((command) => command.command === "debug.restart"));
    assert.ok(debugCatalog.commands.some((command) => command.command === "debug.launchProject"));
    assert.ok(debugCatalog.commands.some((command) => command.command === "debug.launchFile"));
    assert.ok(debugCatalog.commands.some((command) => command.command === "debug.hotReload"));

    const workspacePath = vscode.workspace.workspaceFolders[0].uri.fsPath;
    const targetPath = path.join(workspacePath, "debug-target.cjs");
    readinessServer = http.createServer((_request, response) => {
      response.writeHead(200);
      response.end("ready");
    });
    await new Promise((resolve, reject) => {
      readinessServer.once("error", reject);
      readinessServer.listen(0, "127.0.0.1", resolve);
    });
    const readinessAddress = readinessServer.address();
    assert.ok(readinessAddress && typeof readinessAddress !== "string");
    await vscode.workspace.getConfiguration("esimcp").update("debugHostReadinessUrl", `http://127.0.0.1:${readinessAddress.port}/`, vscode.ConfigurationTarget.Workspace);

    const projectLaunch = await debug("debug.start", {
      workspaceFolder: workspacePath,
      configuration: {
        name: "EsiMCP Integration",
        type: "node",
        request: "launch",
        program: targetPath,
        cwd: workspacePath,
        console: "internalConsole",
      },
    });
    assert.equal(projectLaunch.started, true);
    assert.equal(typeof projectLaunch.sessionId, "string");
    await waitFor(() => vscode.debug.activeDebugSession?.id === projectLaunch.sessionId, "the real project-launch debug session");
    assert.equal(await debug("debug.active.session"), projectLaunch.sessionId);
    assert.deepEqual(await debug("debug.start", {
      workspaceFolder: workspacePath,
      configurationName: "EsiMCP Integration",
    }), { started: false, sessionId: null }, "EsiMCP must not start a second session over an active one");

    await waitFor(() => Boolean(vscode.debug.activeStackItem), "the Node debugger to pause at its debugger statement");
    const pausedEvent = await debug("debug.wait.for.event", { timeoutMs: 5000, type: "paused" });
    assert.equal(pausedEvent.type, "paused");

    const setting = await debug("debug.settings", { setting: "esimcp.serverPort" });
    assert.equal(setting.value, port);
    const variableNames = await debug("debug.list.variable.names", { scope: "local" });
    assert.ok(variableNames.includes("localValue"), `Expected localValue in ${JSON.stringify(variableNames)}`);
    const variableValues = await debug("debug.get.variables.values", { variableNames: ["localValue", "accessToken"], scope: "local" });
    assert.equal(String(variableValues.localValue), "41");
    assert.equal(variableValues.accessToken, "[REDACTED]");
    const evaluated = await debug("debug.evaluate.expression", { expression: "localValue + 1" });
    assert.equal(String(evaluated), "42");

    const addedBreakpoint = await debug("debug.add.breakpoint", { fileFullPath: targetPath, line: 5 });
    assert.equal(addedBreakpoint.fileFullPath, targetPath);
    assert.equal(addedBreakpoint.line, 5);
    const addedLogpoint = await debug("debug.add.logpoint", { fileFullPath: targetPath, line: 6, logMessage: "value={incrementedValue}" });
    assert.equal(addedLogpoint.logMessage, "value={incrementedValue}");
    assert.ok((await debug("debug.list.breakpoints")).length >= 2);
    await debug("debug.remove.breakpoint", { fileFullPath: targetPath, line: 5 });
    await debug("debug.clear.all.breakpoints");
    assert.equal((await debug("debug.list.breakpoints")).length, 0);

    for (const commandId of ["debug.step.over", "debug.step.into", "debug.step.out"]) {
      assert.deepEqual(await debug(commandId), { stepped: true });
      await waitFor(() => Boolean(vscode.debug.activeStackItem), `${commandId} to leave the debugger paused`);
    }

    assert.deepEqual(await debug("debug.continue"), { continued: true });
    assert.equal((await debug("debug.wait.for.event", { timeoutMs: 5000, type: "continued" })).type, "continued");
    assert.deepEqual(await debug("debug.pause"), { paused: true });
    assert.equal((await debug("debug.wait.for.event", { timeoutMs: 5000, type: "paused" })).type, "paused");
    assert.deepEqual(await debug("debug.check.host.readyness"), { ready: true });

    const oldSessionId = projectLaunch.sessionId;
    assert.deepEqual(await debug("debug.restart", { rebuildTaskName: "integration-build" }), { restarted: true });
    const restartedSessionId = await debug("debug.active.session");
    assert.equal(typeof restartedSessionId, "string");
    assert.notEqual(restartedSessionId, oldSessionId);
    assert.deepEqual(await debug("debug.stop"), { stopped: true });
    assert.equal(await debug("debug.active.session"), null);

    const namedLaunch = await debug("debug.start", {
      workspaceFolder: workspacePath,
      configurationName: "EsiMCP Integration",
    });
    assert.equal(namedLaunch.started, true);
    assert.equal(typeof namedLaunch.sessionId, "string");

    const fileLaunch = await debug("debug.launchFile", {
      configurationName: "EsiMCP Integration",
      workspaceFolder: workspacePath,
    });
    assert.equal(fileLaunch.success, true);
    assert.equal(typeof fileLaunch.sessionId, "string");
    assert.notEqual(fileLaunch.sessionId, namedLaunch.sessionId);
    assert.equal(typeof fileLaunch.buildLogPath, "string");
    await require("node:fs/promises").access(fileLaunch.buildLogPath);

    const watchStarted = await debug("debug.hotReload", {
      mode: "watch",
      projectFile: "WatchFixture.csproj",
      workspaceFolder: workspacePath,
    });
    assert.equal(watchStarted.success, true, JSON.stringify(watchStarted));
    assert.equal(watchStarted.mode, "watch");
    assert.equal(await debug("debug.active.session"), null, "Watch runs without a VS Code debugger session");
    const watchUrl = `http://127.0.0.1:${process.env.ESIMCP_WATCH_PORT}/`;
    await waitForHttpText(watchUrl, "watch-before", "the dotnet watch fixture to start");
    const pidBefore = await (await fetch(`${watchUrl}pid`)).text();
    const programPath = path.join(workspacePath, "Program.cs");
    const originalProgram = await require("node:fs/promises").readFile(programPath, "utf8");
    const updatedProgram = originalProgram.replace('"watch-before"', '"watch-after"');
    assert.notEqual(updatedProgram, originalProgram, "The watch fixture source must contain the expected initial value");
    await require("node:fs/promises").writeFile(programPath, updatedProgram, "utf8");
    await waitForHttpText(watchUrl, "watch-after", "dotnet watch to apply the supported method edit");
    const pidAfter = await (await fetch(`${watchUrl}pid`)).text();
    assert.equal(pidAfter, pidBefore, "a supported Hot Reload edit must keep the app process alive");
    const stoppedWatch = await debug("debug.hotReload", { mode: "stopWatch" });
    assert.equal(stoppedWatch.success, true);
    assert.equal(stoppedWatch.stopped, true);

    const rebuiltLaunch = await debug("debug.hotReload", {
      mode: "rebuild",
      configurationName: "EsiMCP Integration",
      workspaceFolder: workspacePath,
    });
    assert.equal(rebuiltLaunch.success, true);
    assert.notEqual(rebuiltLaunch.sessionId, fileLaunch.sessionId);
    assert.equal(typeof rebuiltLaunch.buildLogPath, "string");
    await require("node:fs/promises").access(rebuiltLaunch.buildLogPath);

    assert.deepEqual(await debug("debug.stop"), { stopped: true });
    assert.equal(await debug("debug.active.session"), null);
    await waitFor(() => vscode.debug.activeDebugSession === undefined, "all native launch forms to stop cleanly");
    console.log("EsiMCP native debug integration scenarios passed");
  } finally {
    try {
      await debug("debug.hotReload", { mode: "stopWatch" });
    } catch {
      // The test server may already be shutting down after a failed scenario.
    }
    if (vscode.debug.activeDebugSession) await vscode.debug.stopDebugging(vscode.debug.activeDebugSession);
    if (readinessServer?.listening) await new Promise((resolve) => readinessServer.close(resolve));
  }
}

module.exports = { run };
