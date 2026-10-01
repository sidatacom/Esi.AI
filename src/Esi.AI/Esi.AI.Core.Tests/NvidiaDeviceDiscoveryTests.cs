using Esi.AI.Core.ModelLoading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class NvidiaDeviceDiscoveryTests
{
    [TestMethod]
    public void ParseOutput_WhenIndicesAreSparse_PreservesCudaDeviceOrdinals()
    {
        var devices = NvidiaDeviceDiscovery.ParseOutput(
            "2, NVIDIA GeForce RTX 4090, 550.54.14\n0, NVIDIA RTX A4000, 535.183.01\n");

        CollectionAssert.AreEqual(new[] { "cuda:2", "cuda:0" }, devices.Select(device => device.DeviceId).ToArray());
        Assert.AreEqual("NVIDIA GeForce RTX 4090", devices[0].DeviceCaption);
        Assert.AreEqual("NVIDIA Driver 550.54.14", devices[0].Driver);
    }

    [TestMethod]
    public void ParseOutput_WhenNoDevicesAreReported_ReturnsEmptyCollection()
    {
        var devices = NvidiaDeviceDiscovery.ParseOutput(string.Empty);

        Assert.IsEmpty(devices);
    }

    [TestMethod]
    public void ParseOutput_WhenRowIsInvalid_ThrowsFormatException()
    {
        Assert.ThrowsExactly<FormatException>(() => NvidiaDeviceDiscovery.ParseOutput("not a device row"));
    }
}