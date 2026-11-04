using System.Diagnostics;
using System.Text;
using Esi.AI.Backend.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Backend.Vllm.Xpu.Tests;

[TestClass]
public sealed class BackendProcessLifecycleTests
{
    [TestMethod]
    public void ConfigureStartInfo_WhenStdinIsRedirected_EnablesParentLifetimePipe()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "python3",
            RedirectStandardInput = true
        };

        BackendProcessLifecycle.ConfigureStartInfo(startInfo);

        Assert.AreEqual("1", startInfo.Environment["ESI_PARENT_LIFETIME_PIPE"]);
    }

    [TestMethod]
    public async Task WaitForParentPipeClosureAsync_WhenLifetimeStreamEnds_Completes()
    {
        using var lifetimeStream = new StringReader(string.Empty);

        await BackendProcessLifecycle.WaitForParentPipeClosureAsync(lifetimeStream)
            .WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task WaitForParentPipeClosureAsync_WhenReaderBlocksSynchronously_ReturnsBeforeClosure()
    {
        using var lifetimeStream = new SynchronouslyBlockingTextReader();
        var invocation = Task.Factory.StartNew(
            () => BackendProcessLifecycle.WaitForParentPipeClosureAsync(lifetimeStream),
            CancellationToken.None,
            TaskCreationOptions.None,
            TaskScheduler.Default);

        try
        {
            await lifetimeStream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var waitTask = await invocation.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.IsFalse(waitTask.IsCompleted);
            lifetimeStream.Release();
            await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            lifetimeStream.Release();
        }
    }

    [TestMethod]
    public async Task ConfigureStartInfo_WhenLinux_PreservesChildExitCode()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("sleep 1; exit 7");
        BackendProcessLifecycle.ConfigureStartInfo(startInfo);

        using var process = new Process { StartInfo = startInfo };
        Assert.IsTrue(process.Start());

        await process.WaitForExitAsync();

        Assert.AreEqual(7, process.ExitCode);
    }

    [TestMethod]
    public async Task StopAsync_WhenBackendHasChildProcesses_StopsTheEntireProcessGroup()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("sleep 60 & wait");
        BackendProcessLifecycle.ConfigureStartInfo(startInfo);

        using var process = new Process { StartInfo = startInfo };
        Assert.IsTrue(process.Start());

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.IsFalse(process.HasExited);

        await BackendProcessLifecycle.StopAsync(process);

        Assert.IsTrue(process.HasExited);
    }

    [TestMethod]
    public async Task StopAsync_WhenParentPipeCloses_GracefullyStopsItsWorkers()
    {
        if (!OperatingSystem.IsLinux())
            return;

        const string pythonCode = """
            import asyncio
            import multiprocessing
            import time
            from esi_process_lifecycle import install_parent_lifetime_handlers

            def wait_forever(shutdown_event):
                shutdown_event.wait(60)

            async def main():
                stopped = asyncio.Event()
                install_parent_lifetime_handlers(stopped)
                context = multiprocessing.get_context("fork")
                worker_shutdown = context.Event()
                worker = context.Process(target=wait_forever, args=(worker_shutdown,))
                worker.start()
                print("bridge-ready", flush=True)
                await stopped.wait()
                worker_shutdown.set()
                worker.join(5)
                if worker.is_alive():
                    worker.kill()
                    worker.join(5)
                    raise RuntimeError("The backend worker did not stop.")
                print("graceful-shutdown", flush=True)

            asyncio.run(main())
            """;
        var pythonDirectory = Path.Combine(AppContext.BaseDirectory, "Python");
        var startInfo = new ProcessStartInfo
        {
            FileName = "python3",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(pythonCode);
        startInfo.Environment["PYTHONPATH"] = pythonDirectory;
        BackendProcessLifecycle.ConfigureStartInfo(startInfo);

        using var process = new Process { StartInfo = startInfo };
        Assert.IsTrue(process.Start());
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            Assert.AreEqual("bridge-ready", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            await BackendProcessLifecycle.StopAsync(process);

            var output = await standardOutput;
            var error = await standardError;
            Assert.AreEqual(0, process.ExitCode, error);
            Assert.Contains("graceful-shutdown", output);
        }
        finally
        {
            await BackendProcessLifecycle.StopAsync(process);
        }
    }

    [TestMethod]
    public async Task ParentLifetimePipe_WhenBridgeHostExits_StopsItsWorkerProcessGroup()
    {
        if (!OperatingSystem.IsLinux())
            return;

        const string bridgeCode = """
            import asyncio
            import multiprocessing
            import os
            import time
            from esi_process_lifecycle import install_parent_lifetime_handlers

            def wait_forever(shutdown_event):
                shutdown_event.wait(60)

            async def main():
                stopped = asyncio.Event()
                install_parent_lifetime_handlers(stopped)
                context = multiprocessing.get_context("fork")
                worker_shutdown = context.Event()
                worker = context.Process(target=wait_forever, args=(worker_shutdown,))
                worker.start()
                print(f"{os.getpid()}:{worker.pid}", flush=True)
                await stopped.wait()
                worker_shutdown.set()
                worker.join(5)
                if worker.is_alive():
                    worker.kill()
                    worker.join(5)
                    raise RuntimeError("The backend worker did not stop.")

            asyncio.run(main())
            """;
        const string launcherCode = """
            import os
            import subprocess
            import sys

            environment = dict(os.environ)
            environment["ESI_PARENT_LIFETIME_PIPE"] = "1"
            child = subprocess.Popen(
                [sys.executable, "-c", sys.argv[1]],
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                text=True,
                env=environment,
            )
            ready = child.stdout.readline()
            if not ready:
                raise RuntimeError("The bridge child did not become ready.")
            print(ready.strip(), flush=True)
            """;
        var pythonDirectory = Path.Combine(AppContext.BaseDirectory, "Python");
        var startInfo = new ProcessStartInfo
        {
            FileName = "python3",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(launcherCode);
        startInfo.ArgumentList.Add(bridgeCode);
        startInfo.Environment["PYTHONPATH"] = pythonDirectory;
        BackendProcessLifecycle.ConfigureStartInfo(startInfo);

        using var process = new Process { StartInfo = startInfo };
        Assert.IsTrue(process.Start());
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            var processIdsLine = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (processIdsLine is null)
                Assert.Fail("The backend bridge did not report its process IDs.");
            var processIds = processIdsLine.Split(':');
            Assert.AreEqual(2, processIds.Length);
            Assert.IsTrue(int.TryParse(processIds[0], out var bridgeProcessId), $"Invalid bridge process ID: {processIds[0]}.");
            Assert.IsTrue(int.TryParse(processIds[1], out var workerProcessId), $"Invalid worker process ID: {processIds[1]}.");

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.AreEqual(0, process.ExitCode, await standardError);
            await WaitForProcessesToExitOrBecomeZombiesAsync([bridgeProcessId, workerProcessId], TimeSpan.FromSeconds(5));
        }
        finally
        {
            await BackendProcessLifecycle.StopAsync(process);
        }
    }

    [TestMethod]
    public async Task XpuBridge_WhenLaunchingThreadExits_RemainsAliveUntilManagedStop()
    {
        if (!OperatingSystem.IsLinux())
            return;

        const string pythonCode = """
            import importlib.util
            import sys
            import time
            import types

            sys.modules["grpc"] = types.ModuleType("grpc")
            sys.modules["inference_pb2"] = types.ModuleType("inference_pb2")
            inference_pb2_grpc = types.ModuleType("inference_pb2_grpc")
            inference_pb2_grpc.InferenceServicer = type("InferenceServicer", (), {})
            sys.modules["inference_pb2_grpc"] = inference_pb2_grpc

            spec = importlib.util.spec_from_file_location("xpu_inference_server", sys.argv[1])
            bridge = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(bridge)
            print("bridge-imported", flush=True)
            time.sleep(60)
            """;
        var bridgePath = Path.Combine(AppContext.BaseDirectory, "Python", "inference_server.py");
        Assert.IsTrue(File.Exists(bridgePath), $"The XPU bridge script was not deployed to '{bridgePath}'.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "python3",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(pythonCode);
        startInfo.ArgumentList.Add(bridgePath);
        startInfo.Environment["PYTHONPATH"] = Path.GetDirectoryName(bridgePath)!;
        startInfo.Environment.Remove("VLLM_TARGET_DEVICE");
        BackendProcessLifecycle.ConfigureStartInfo(startInfo);

        using var process = new Process { StartInfo = startInfo };
        using var bridgeImported = new ManualResetEventSlim();
        var startOutput = new StringBuilder();
        Exception? startError = null;
        var processStarted = false;
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data == "bridge-imported")
                bridgeImported.Set();
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
                lock (startOutput)
                    startOutput.AppendLine(eventArgs.Data);
        };

        var launchingThread = new Thread(() =>
        {
            try
            {
                processStarted = process.Start();
                if (!processStarted)
                    throw new InvalidOperationException("The XPU bridge process did not start.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!bridgeImported.Wait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("The XPU bridge did not finish importing.");
            }
            catch (Exception exception)
            {
                startError = exception;
            }
        });

        try
        {
            launchingThread.Start();
            Assert.IsTrue(launchingThread.Join(TimeSpan.FromSeconds(20)), "The bridge launching thread did not finish.");
            if (startError is not null)
            {
                lock (startOutput)
                    Assert.Fail($"{startError}{Environment.NewLine}{startOutput}");
            }

            Assert.IsFalse(process.HasExited, "The XPU bridge exited when its launching thread ended.");
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            Assert.IsFalse(process.HasExited, "The XPU bridge did not remain alive after import.");
        }
        finally
        {
            if (processStarted)
                await BackendProcessLifecycle.StopAsync(process);
        }
    }

    [TestMethod]
    public async Task XpuBridge_WhenStdoutPipeIsBroken_RedirectsItBeforeWorkerStartup()
    {
        if (!OperatingSystem.IsLinux())
            return;

        const string pythonCode = """
            import importlib.util
            import multiprocessing
            import os
            import sys
            import types

            sys.modules["grpc"] = types.ModuleType("grpc")
            sys.modules["inference_pb2"] = types.ModuleType("inference_pb2")
            inference_pb2_grpc = types.ModuleType("inference_pb2_grpc")
            inference_pb2_grpc.InferenceServicer = type("InferenceServicer", (), {})
            sys.modules["inference_pb2_grpc"] = inference_pb2_grpc

            spec = importlib.util.spec_from_file_location("xpu_inference_server", sys.argv[1])
            bridge = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(bridge)

            read_fd, write_fd = os.pipe()
            os.dup2(write_fd, sys.stdout.fileno())
            os.close(read_fd)
            os.close(write_fd)
            sys.stdout.write("buffered stdout")
            stdout_fd = sys.stdout.fileno()
            bridge._redirect_stdout_to_stderr()
            os.close(stdout_fd)
            sys.stdout.write("stdout redirected")
            sys.stdout.flush()

            worker = multiprocessing.get_context("fork").Process(target=lambda: None)
            worker.start()
            worker.join(5)
            if worker.exitcode != 0:
                raise RuntimeError(f"Worker exited with code {worker.exitcode}.")
            print("worker-started", file=sys.stderr, flush=True)
            """;
        var bridgePath = Path.Combine(AppContext.BaseDirectory, "Python", "inference_server.py");
        Assert.IsTrue(File.Exists(bridgePath), $"The XPU bridge script was not deployed to '{bridgePath}'.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "python3",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(pythonCode);
        startInfo.ArgumentList.Add(bridgePath);
        startInfo.Environment["PYTHONPATH"] = Path.GetDirectoryName(bridgePath)!;
        startInfo.Environment.Remove("VLLM_TARGET_DEVICE");
        BackendProcessLifecycle.ConfigureStartInfo(startInfo);

        using var process = new Process { StartInfo = startInfo };
        Assert.IsTrue(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
                await BackendProcessLifecycle.StopAsync(process);
        }

        await standardOutput;
        var errorOutput = await standardError;
        Assert.AreEqual(0, process.ExitCode, errorOutput);
        Assert.Contains("stdout redirected", errorOutput);
        Assert.Contains("worker-started", errorOutput);
    }

    private static async Task WaitForProcessesToExitOrBecomeZombiesAsync(int[] processIds, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            if (processIds.All(IsProcessExitedOrZombie))
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        var remainingProcessIds = processIds.Where(processId => !IsProcessExitedOrZombie(processId));
        Assert.Fail($"Backend processes did not stop after parent exit: {string.Join(", ", remainingProcessIds)}.");
    }

    private static bool IsProcessExitedOrZombie(int processId)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{processId}/stat");
            var closingParenthesis = stat.LastIndexOf(')');
            return closingParenthesis >= 0 &&
                closingParenthesis + 2 < stat.Length &&
                stat[closingParenthesis + 2] is 'Z' or 'X';
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
    }

    private sealed class SynchronouslyBlockingTextReader : TextReader
    {
        private readonly ManualResetEventSlim release = new();

        public TaskCompletionSource<bool> ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult(true);
            release.Wait(cancellationToken);
            return Task.FromResult(string.Empty);
        }

        public void Release() => release.Set();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                release.Dispose();

            base.Dispose(disposing);
        }
    }
}
