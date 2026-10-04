using Esi.AI.Core.ModelLoading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class OpenVinoDiagnosticsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Diagnose_WhenOpenVinoLoadIsActive_ReturnsDeferredDiagnosticsWithoutNativeAccess()
    {
        var gate = new OpenVinoLoadGate();
        var diagnosticsService = new OpenVinoDiagnosticsService(gate);
        Assert.IsTrue(gate.TryEnter());

        var diagnostics = diagnosticsService.Diagnose();

        Assert.IsFalse(diagnostics.IsGpuReady);
        Assert.IsFalse(diagnostics.IsNpuReady);
        Assert.AreEqual("openvino-load-in-progress", diagnostics.Checks.Single().Id);
    }

    [TestMethod]
    public void SelectIntelGpuName_WhenBattlemageAndIntegratedGpuExist_PrefersBattlemage()
    {
        var name = OpenVinoDiagnosticsService.SelectIntelGpuName(
        [
            "00:02.0 VGA compatible controller [0300]: Intel Corporation CoffeeLake-S GT2 [UHD Graphics 630] [8086:3e98]",
            "03:00.0 VGA compatible controller [0300]: Intel Corporation Battlemage G31 [Intel Graphics] [8086:e223]"
        ]);

        Assert.AreEqual("Intel Corporation Battlemage G31 [Intel Graphics] [8086:e223]", name);
    }

    [TestMethod]
    public void Diagnose_WhenNativeGpuNameIsAvailable_UsesOpenVinoDeviceName()
    {
        var diagnostics = new OpenVinoDiagnosticsService().Diagnose(() => "Intel(R) Arc(TM) B580 Graphics");

        Assert.AreEqual("Intel(R) Arc(TM) B580 Graphics", diagnostics.Devices.Single(device => device.Id == "GPU").Name);
    }

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
