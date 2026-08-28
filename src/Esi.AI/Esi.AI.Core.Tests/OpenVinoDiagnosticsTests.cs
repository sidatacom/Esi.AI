using Esi.AI.Core.ModelLoading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class OpenVinoDiagnosticsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("OpenVINO.Integration")]
    public void Diagnose_WithNativeRuntime_EnumeratesDevices()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ESI_OPENVINO_RUN_HARDWARE_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Inconclusive("Set ESI_OPENVINO_RUN_HARDWARE_TESTS=1 to run native OpenVINO diagnostics.");
            return;
        }

        var diagnostics = new OpenVinoDiagnosticsService().Diagnose();

        TestContext.WriteLine($"GPU ready: {diagnostics.IsGpuReady}");
        TestContext.WriteLine($"NPU ready: {diagnostics.IsNpuReady}");
        TestContext.WriteLine($"Devices: {string.Join(", ", diagnostics.Devices.Select(device => $"{device.Id}={device.Name}"))}");
        foreach (var check in diagnostics.Checks)
            TestContext.WriteLine($"{check.Id}: {check.IsAvailable} - {check.Detail}");

        Assert.IsNull(diagnostics.Error);
        Assert.IsNotEmpty(diagnostics.Checks);
    }
}
