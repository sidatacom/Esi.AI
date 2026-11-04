using Esi.AI.Backend.Worker;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Backend.Vllm.Xpu.Tests;

[TestClass]
public sealed class BackendWorkerDeviceDiscoveryTests
{
    [TestMethod]
    public async Task DiscoverDevicesAsync_VllmXpu_UsesPythonXpuDevices()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var originalPythonRoot = Environment.GetEnvironmentVariable("ESI_PYTHON_ENV_ROOT");
        var originalVllmPython = Environment.GetEnvironmentVariable("ESI_VLLM_PYTHON_EXECUTABLE");
        var originalReferencePython = Environment.GetEnvironmentVariable("ESI_PYTHON_REFERENCE_EXECUTABLE");
        var temporaryRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var applicationDirectory = Path.Combine(temporaryRoot, "application");
        try
        {
            Environment.SetEnvironmentVariable("ESI_PYTHON_ENV_ROOT", temporaryRoot);
            Environment.SetEnvironmentVariable("ESI_VLLM_PYTHON_EXECUTABLE", null);
            Environment.SetEnvironmentVariable("ESI_PYTHON_REFERENCE_EXECUTABLE", null);
            Directory.CreateDirectory(applicationDirectory);

            var pythonExecutable = Path.Combine(
                PythonBackendProvisioner.GetDefaultEnvironmentPath(ConfigurationBackend.Vllm, ["xpu:0"]),
                "bin",
                "python");
            Directory.CreateDirectory(Path.GetDirectoryName(pythonExecutable)!);
            await File.WriteAllTextAsync(pythonExecutable, """
                #!/bin/sh
                printf 'cuda:0\tTest NVIDIA Device\tNVIDIA\tCUDA\nxpu:0\tTest Intel Device\tIntel\tLevel Zero\n'
                """);
            File.SetUnixFileMode(pythonExecutable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            var request = new BackendWorkerRequest(
                "discover-devices",
                ConfigurationBackend.Vllm,
                ApplicationDirectory: applicationDirectory,
                TimeoutSeconds: 5,
                BackendVariantId: "vllm.xpu");
            var response = await Program.DiscoverDevicesAsync(request, applicationDirectory, TimeSpan.FromSeconds(5));

            Assert.IsTrue(response.Succeeded, response.Error);
            Assert.IsNotNull(response.Devices);
            var device = response.Devices.Single();
            Assert.AreEqual("xpu:0", device.Route);
            Assert.AreEqual("Test Intel Device", device.Label);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ESI_PYTHON_ENV_ROOT", originalPythonRoot);
            Environment.SetEnvironmentVariable("ESI_VLLM_PYTHON_EXECUTABLE", originalVllmPython);
            Environment.SetEnvironmentVariable("ESI_PYTHON_REFERENCE_EXECUTABLE", originalReferencePython);
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}