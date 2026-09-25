using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Backend.Vllm.Cuda12;
using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Backend.Vllm.Cuda12.Tests;

[TestClass]
public sealed class VllmCuda12BackendTests
{
    [TestMethod]
    public void Descriptor_WhenModuleIsCreated_UsesStableCuda12Identity()
    {
        var descriptor = new VllmCuda12BackendModule().Descriptor;

        Assert.AreEqual("vllm.cuda12", descriptor.Id);
        Assert.AreEqual(ConfigurationBackend.Vllm, descriptor.Family);
        Assert.AreEqual("CUDA12", descriptor.Route);
    }

    [TestMethod]
    public void AddServices_WhenModuleIsRegistered_ProvidesUnifiedBackendRuntime()
    {
        var services = new ServiceCollection();
        new VllmCuda12BackendModule().AddServices(services);
        using var provider = services.BuildServiceProvider();

        var runtime = provider.GetRequiredService<IBackendRuntime>();

        Assert.IsInstanceOfType<VllmCuda12Runtime>(runtime);
        Assert.AreEqual("vllm.cuda12", runtime.Descriptor.Id);
    }

    [TestMethod]
    public async Task LoadAsync_WhenVariantDoesNotMatch_RejectsBeforePythonPreparation()
    {
        using var runtime = new VllmCuda12Runtime();
        using var configuration = JsonDocument.Parse("{}");
        var request = new BackendLoadRequest("owner/model", "vllm.xpu", configuration.RootElement);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() => runtime.LoadAsync(request));

        Assert.Contains("vllm.cuda12", exception.Message);
    }

    [TestMethod]
    public async Task LoadAsync_WhenConfigurationIsInvalid_RejectsBeforePythonPreparation()
    {
        using var runtime = new VllmCuda12Runtime();
        using var configuration = JsonSerializer.SerializeToDocument(
            new PythonInferenceLoadRequest("owner/model", ConfigurationBackend.Vllm, TensorParallelSize: 0));
        var request = new BackendLoadRequest("owner/model", "vllm.cuda12", configuration.RootElement);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => runtime.LoadAsync(request));
    }

    [TestMethod]
    public async Task LoadAsync_WhenAbsoluteModelPathDoesNotExist_RejectsBeforePythonPreparation()
    {
        using var runtime = new VllmCuda12Runtime();
        var modelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "model");
        using var configuration = JsonSerializer.SerializeToDocument(
            new PythonInferenceLoadRequest(modelPath, ConfigurationBackend.Vllm));
        var request = new BackendLoadRequest(modelPath, "vllm.cuda12", configuration.RootElement);

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => runtime.LoadAsync(request));
    }

    [TestMethod]
    public void ToGrpcRequest_WhenGenerationContainsTools_MapsNormalizedMessageAndToolContracts()
    {
        using var parameters = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}}}");
        using var toolChoice = JsonDocument.Parse("\"auto\"");
        var request = new OpenAiBackendChatRequest(
            "vLLM",
            "owner/model",
            null,
            [],
            [new ChatMessage("user", "Weather?", ToolCallId: "call-1")],
            null,
            new ChatGenerationOptions(
                MaxTokens: 64,
                Seed: 42,
                StopSequences: ["END"],
                Tools: [new OpenAiToolDefinition("function", new OpenAiToolFunction("weather", "Look up weather", parameters.RootElement))],
                ToolChoice: toolChoice.RootElement),
            null);

        var mapped = VllmCuda12GrpcMapper.ToGrpcRequest(request, "model-id", "request-id");

        Assert.AreEqual("vllm", VllmCuda12GrpcMapper.ToGrpcRequest(new PythonInferenceLoadRequest("owner/model", ConfigurationBackend.Vllm)).Engine);
        Assert.AreEqual("request-id", mapped.RequestId);
        Assert.AreEqual("user", mapped.Messages[0].Role);
        Assert.AreEqual("call-1", mapped.Messages[0].ToolCallId);
        Assert.AreEqual("weather", mapped.Tools[0].Name);
        Assert.AreEqual("\"auto\"", mapped.ToolChoiceJson);
        Assert.AreEqual(42, mapped.Seed);
        Assert.AreEqual("END", mapped.StopSequences[0]);
    }
}