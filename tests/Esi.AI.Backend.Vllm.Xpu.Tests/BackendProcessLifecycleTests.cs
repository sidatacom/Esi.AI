using System.Diagnostics;
using System.Text;
using Esi.AI.Backend.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Backend.Vllm.Xpu.Tests;

[TestClass]
public sealed class BackendProcessLifecycleTests
{
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
}
