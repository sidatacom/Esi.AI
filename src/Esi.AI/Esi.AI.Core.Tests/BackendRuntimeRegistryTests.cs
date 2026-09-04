using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class BackendRuntimeRegistryTests
{
    [TestMethod]
    [DataRow("cuda", ConfigurationBackend.Llama)]
    [DataRow("OpenVINO", ConfigurationBackend.OpenVino)]
    [DataRow("vllm", ConfigurationBackend.Vllm)]
    [DataRow("SGLANG", ConfigurationBackend.Sglang)]
    [DataRow("dotllm", ConfigurationBackend.DotLlm)]
    public void Normalize_WhenBackendAliasIsProvided_ReturnsCanonicalBackend(string backend, ConfigurationBackend expected)
    {
        Assert.AreEqual(expected, BackendRuntimeRegistry.Normalize(backend));
    }

    [TestMethod]
    public void Resolve_WhenSglangAdapterIsNotRegistered_UsesPythonAdapter()
    {
        using var adapter = new TestAdapter(ConfigurationBackend.Vllm);
        using var registry = new BackendRuntimeRegistry([adapter]);

        Assert.AreSame(adapter, registry.Resolve(ConfigurationBackend.Sglang));
    }

    [TestMethod]
    public void Resolve_WhenBackendIsUnsupported_ThrowsArgumentException()
    {
        using var registry = new BackendRuntimeRegistry([]);

        Assert.ThrowsExactly<ArgumentException>(() => registry.Resolve(ConfigurationBackend.Llama));
    }

    private sealed class TestAdapter(ConfigurationBackend backend) : IBackendRuntimeAdapter
    {
        public ConfigurationBackend Backend => backend;

        public string RuntimeName => "Test";

        public ModelLoadStatus GetStatus() => new(null, string.Empty, 0, 0, 0, 0, [], null, string.Empty, new Dictionary<string, float>(), false, []);

        public bool SupportsImageInput(string? modelPath) => false;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
