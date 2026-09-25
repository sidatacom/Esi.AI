using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Backend.Vllm.Xpu;
using Esi.AI.Backend.Vllm.Xpu.Grpc;
using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GrpcGenerateResponse = Esi.AI.Backend.Vllm.Xpu.Grpc.GenerateResponse;
using ModelChatMessage = Esi.AI.Models.ChatMessage;

namespace Esi.AI.Backend.Vllm.Xpu.Tests;

[TestClass]
public sealed class VllmXpuBackendTests
{
    [TestMethod]
    public void Descriptor_WhenModuleIsCreated_UsesStableXpuIdentity()
    {
        var descriptor = new VllmXpuBackendModule().Descriptor;

        Assert.AreEqual("vllm.xpu", descriptor.Id);
        Assert.AreEqual(ConfigurationBackend.Vllm, descriptor.Family);
        Assert.AreEqual("XPU", descriptor.Route);
    }

    [TestMethod]
    public void AddServices_WhenModuleIsRegistered_ProvidesUnifiedBackendRuntime()
    {
        var services = new ServiceCollection();
        new VllmXpuBackendModule().AddServices(services);
        using var provider = services.BuildServiceProvider();

        var runtime = provider.GetRequiredService<IBackendRuntime>();

        Assert.IsInstanceOfType<VllmXpuRuntime>(runtime);
        Assert.AreEqual("vllm.xpu", runtime.Descriptor.Id);
    }

    [TestMethod]
    public void NormalizeLoadConfiguration_WhenDeviceIsOmitted_UsesXpuDefaultAndOuterModelPath()
    {
        using var configuration = JsonDocument.Parse("{}");
        var request = new BackendLoadRequest("org/model", "vllm.xpu", configuration.RootElement);

        var normalized = VllmXpuRuntime.NormalizeLoadConfiguration(request);

        Assert.AreEqual("org/model", normalized.ModelPath);
        Assert.AreEqual(ConfigurationBackend.Vllm, normalized.Backend);
        Assert.AreEqual("xpu:0", normalized.Device);
    }

    [TestMethod]
    public void NormalizeLoadConfiguration_WhenCudaDeviceIsSpecified_RejectsNonXpuRoute()
    {
        using var configuration = JsonDocument.Parse("{\"device\":\"cuda:0\"}");
        var request = new BackendLoadRequest("org/model", "vllm.xpu", configuration.RootElement);

        var exception = Assert.ThrowsExactly<ArgumentException>(() => VllmXpuRuntime.NormalizeLoadConfiguration(request));

        Assert.Contains("xpu:<index>", exception.Message);
    }

    [TestMethod]
    public void ApplyDeviceEnvironment_WhenXpuRoutesAreSelected_SetsXpuBootstrapVariables()
    {
        var startInfo = new ProcessStartInfo();

        VllmXpuRuntime.ApplyDeviceEnvironment(startInfo, ["xpu:1"], true, false);

        Assert.AreEqual("xpu", startInfo.Environment["VLLM_TARGET_DEVICE"]);
        Assert.AreEqual("level_zero:0", startInfo.Environment["ONEAPI_DEVICE_SELECTOR"]);
        Assert.AreEqual("1", startInfo.Environment["VLLM_XPU_ENABLE_XPU_GRAPH"]);
        Assert.AreEqual("spawn", startInfo.Environment["VLLM_WORKER_MULTIPROC_METHOD"]);
        Assert.AreEqual(string.Empty, startInfo.Environment["CUDA_VISIBLE_DEVICES"]);
    }

    [TestMethod]
    public void ApplyDeviceEnvironment_WhenCudaRouteIsSelected_RejectsBeforeStartingProcess()
    {
        var startInfo = new ProcessStartInfo();

        Assert.ThrowsExactly<ArgumentException>(() =>
            VllmXpuRuntime.ApplyDeviceEnvironment(startInfo, ["cuda:0"], false, false));
    }

    [TestMethod]
    public void ToGrpcRequest_WhenLoadSettingsAreProvided_MapsXpuModelOptions()
    {
        var request = new PythonInferenceLoadRequest(
            "org/model",
            ConfigurationBackend.Vllm,
            Device: "xpu:0",
            Devices: ["xpu:0", "xpu:1"],
            GpuMemoryUtilization: 85,
            EnableXpuGraph: true);

        var grpcRequest = VllmXpuGrpcMapper.ToGrpcRequest(request);

        Assert.AreEqual("vllm", grpcRequest.Engine);
        Assert.AreEqual("org/model", grpcRequest.ModelPath);
        Assert.AreEqual("xpu:0", grpcRequest.Device);
        CollectionAssert.AreEqual(new[] { "xpu:0", "xpu:1" }, grpcRequest.Devices.ToArray());
        Assert.AreEqual(.85f, grpcRequest.GpuMemoryUtilization);
        Assert.IsTrue(grpcRequest.EnableXpuGraph);
    }

    [TestMethod]
    public void ToGrpcRequest_WhenGenerationOptionsContainTools_MapsModelsContracts()
    {
        using var parameters = JsonDocument.Parse("{\"type\":\"object\"}");
        using var choice = JsonDocument.Parse("\"auto\"");
        var tool = new OpenAiToolDefinition("function", new OpenAiToolFunction("lookup", "Find a value", parameters.RootElement));
        var options = new ChatGenerationOptions(Seed: 29, Tools: [tool], ToolChoice: choice.RootElement);

        var grpcRequest = VllmXpuGrpcMapper.ToGrpcRequest([new ModelChatMessage("user", "hello")], "org/model", options);

        Assert.AreEqual(29, grpcRequest.Seed);
        Assert.AreEqual("lookup", grpcRequest.Tools.Single().Name);
        Assert.AreEqual("{\"type\":\"object\"}", grpcRequest.Tools.Single().ParametersJson);
        Assert.AreEqual("\"auto\"", grpcRequest.ToolChoiceJson);
    }

    [TestMethod]
    public async Task GenerateAsync_WhenBridgeStreamsDeltas_ReturnsNormalizedResultAndTokens()
    {
        using var session = new VllmXpuChatSession(
            (_, cancellationToken) => StreamResponses(cancellationToken),
            "org/model");
        var deltas = new List<string>();

        var result = await session.GenerateAsync(
            [new ModelChatMessage("user", "hello")],
            new ChatGenerationOptions(),
            delta =>
            {
                deltas.Add(delta);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.AreEqual("Hello there", result.Text);
        Assert.AreEqual(4, result.TokenCount);
        Assert.AreEqual(11, result.PromptTokenCount);
        Assert.AreEqual(20d, result.TokensPerSecond);
        Assert.AreEqual("Hello", deltas[0]);
        Assert.AreEqual(" there", deltas[1]);
    }

    private static async IAsyncEnumerable<GrpcGenerateResponse> StreamResponses(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new GrpcGenerateResponse { Delta = "Hello", GeneratedTokens = 2, PromptTokens = 11 };
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new GrpcGenerateResponse { Delta = " there", Finished = true, GeneratedTokens = 4, PromptTokens = 11, TokensPerSecond = 20 };
    }
}
