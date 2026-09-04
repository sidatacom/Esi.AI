using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class BackendPrerequisiteProvisionerTests
{
    [DataTestMethod]
    [DataRow(ConfigurationBackend.OpenVino, "OpenVINO")]
    [DataRow(ConfigurationBackend.DotLlm, "dotLLM")]
    public async Task PrepareAsync_NativeBackend_ReturnsBundledRuntime(ConfigurationBackend backend, string runtimeName)
    {
        var provisioner = new BackendPrerequisiteProvisioner();

        var result = await provisioner.PrepareAsync(backend);

        Assert.AreEqual(backend, result.Backend);
        Assert.IsFalse(result.EnvironmentCreated);
        Assert.IsNull(result.PythonExecutable);
        StringAssert.Contains(result.Message, runtimeName);
    }

    [TestMethod]
    public async Task PrepareAsync_UnsupportedBackend_ThrowsArgumentException()
    {
        var provisioner = new BackendPrerequisiteProvisioner();

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => provisioner.PrepareAsync((ConfigurationBackend)999));
    }

    [DataTestMethod]
    [DataRow(ConfigurationBackend.OpenVino, "OpenVINO")]
    [DataRow(ConfigurationBackend.DotLlm, "dotLLM")]
    public async Task DiagnoseAsync_NativeBackend_ReturnsReadyBundledRuntime(ConfigurationBackend backend, string runtimeName)
    {
        var result = await new BackendPrerequisiteProvisioner().DiagnoseAsync(backend);

        Assert.IsTrue(result.IsReady);
        Assert.AreEqual(runtimeName, result.BackendName);
        Assert.AreEqual(1, result.Checks.Count);
        Assert.IsTrue(result.Checks[0].IsAvailable);
    }

    [TestMethod]
    public async Task DiagnoseAsync_LlamaSyclRoute_ReportsNativeRuntimeAndToolchainRequirements()
    {
        var result = await new BackendPrerequisiteProvisioner().DiagnoseAsync(
            ConfigurationBackend.Llama,
            devices: ["sycl:0"]);

        Assert.AreEqual("LLama", result.BackendName);
        Assert.IsNotNull(result.Checks.Single(check => check.Id == "sycl-runtime"));
        Assert.IsNotNull(result.Checks.Single(check => check.Id == "intel-level-zero"));
        Assert.IsTrue(result.Checks.Single(check => check.Id == "oneapi-build-toolchain").IsOptional);
    }

    [TestMethod]
    public async Task PrepareAsync_LlamaSyclRoute_ThrowsWhenNativeRuntimeIsMissing()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-sycl-{Guid.NewGuid():N}");

        try
        {
            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                new BackendPrerequisiteProvisioner().PrepareAsync(
                    ConfigurationBackend.Llama,
                    applicationDirectory: applicationDirectory,
                    devices: ["sycl:0"]));

            StringAssert.Contains(exception.Message, "SYCL 16 native runtime");
        }
        finally
        {
            if (Directory.Exists(applicationDirectory))
                Directory.Delete(applicationDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DiagnoseAsync_MissingPythonEnvironment_ReturnsFailedChecks()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = await new BackendPrerequisiteProvisioner().DiagnoseAsync(
            ConfigurationBackend.Vllm,
            "/missing/python",
            applicationDirectory);

        Assert.IsFalse(result.IsReady);
        Assert.IsFalse(result.Checks.Single(check => check.Id == "requirements-file").IsAvailable);
        Assert.IsFalse(result.Checks.Single(check => check.Id == "python-environment").IsAvailable);
    }
}