using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class BackendReferenceModelTests
{
    [TestMethod]
    [TestCategory("BackendReference")]
    public async Task LoadReferenceModel_Llama_GeneratesResponse()
    {
        var modelPath = GetConfiguredPath("ESI_LLAMA_MODEL_PATH");
        if (modelPath is null)
        {
            Assert.Inconclusive("Set ESI_LLAMA_MODEL_PATH to run the LLama reference model test.");
            return;
        }

        using var loader = new LlamaModelLoader();
        await loader.LoadAsync(modelPath, "CPU", 0);
        using var session = loader.CreateChatSession("Answer briefly.");
        var response = await session.GenerateAsync([new ChatMessage("user", "Reply with exactly: LLama reference passed.")]);

        Assert.IsFalse(string.IsNullOrWhiteSpace(response));
    }

    [TestMethod]
    [TestCategory("BackendReference")]
    public async Task LoadReferenceModel_OpenVino_GeneratesResponse()
    {
        var modelPath = GetConfiguredPath("ESI_OPENVINO_MODEL_PATH");
        if (modelPath is null)
        {
            Assert.Inconclusive("Set ESI_OPENVINO_MODEL_PATH to run the OpenVINO reference model test.");
            return;
        }

        var device = Environment.GetEnvironmentVariable("ESI_OPENVINO_DEVICE") ?? "GPU.0";
        using var loader = new OpenVinoModelLoader();
        await loader.LoadAsync(
            modelPath,
            device,
            generationOptions: new OpenVinoGenerationOptions(16, 0, 1, false, 1, 1));
        using var session = loader.CreateChatSession();
        var response = session.Generate("Reply with exactly: OpenVINO reference passed.");

        Assert.IsFalse(string.IsNullOrWhiteSpace(response));
    }

    [TestMethod]
    [TestCategory("BackendReference")]
    public async Task LoadReferenceModel_Vllm_GeneratesResponse()
    {
        await RunPythonReferenceTestAsync(ConfigurationBackend.Vllm, "ESI_VLLM_REFERENCE_MODEL", "ESI_VLLM_REFERENCE_PORT");
    }

    [TestMethod]
    [TestCategory("BackendReference")]
    public async Task LoadReferenceModel_Sglang_GeneratesResponse()
    {
        await RunPythonReferenceTestAsync(ConfigurationBackend.Sglang, "ESI_SGLANG_REFERENCE_MODEL", "ESI_SGLANG_REFERENCE_PORT");
    }

    [TestMethod]
    [TestCategory("BackendReference")]
    public async Task LoadReferenceModel_DotLlm_GeneratesResponse()
    {
        var modelPath = GetConfiguredPath("ESI_DOTLLM_MODEL_PATH");
        if (modelPath is null)
        {
            Assert.Inconclusive("Set ESI_DOTLLM_MODEL_PATH to run the dotLLM reference model test.");
            return;
        }

        using var runtime = new DotLlmInProcessRuntime();
        await runtime.LoadAsync(new DotLlmLoadRequest(modelPath));
        using var session = runtime.CreateChatSession();
        var result = await session.GenerateWithStatsAsync([new ChatMessage("user", "Reply with exactly: dotLLM reference passed.")]);

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Text));
    }

    private static async Task RunPythonReferenceTestAsync(
        ConfigurationBackend backend,
        string modelVariable,
        string portVariable)
    {
        var modelId = Environment.GetEnvironmentVariable(modelVariable);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            Assert.Inconclusive($"Set {modelVariable} to run the {backend} reference model test.");
            return;
        }

        var port = int.TryParse(Environment.GetEnvironmentVariable(portVariable), out var configuredPort)
            ? configuredPort
            : backend == ConfigurationBackend.Vllm ? 18000 : 18001;
        var device = Environment.GetEnvironmentVariable($"ESI_{backend.ToString().ToUpperInvariant()}_DEVICE") ?? "cuda:0";
        using var server = new PythonInferenceServer();
        await server.LoadAsync(new PythonInferenceLoadRequest(
            modelId,
            backend,
            Environment.GetEnvironmentVariable("ESI_PYTHON_REFERENCE_EXECUTABLE") ?? "python3",
            Port: port,
            MaxModelLength: 2048,
            TensorParallelSize: 1,
            TrustRemoteCode: true,
            EnforceEager: true,
            Device: device));
        using var session = server.CreateChatSession();
        var result = await session.GenerateWithStatsAsync([new ChatMessage("user", $"Reply with exactly: {backend} reference passed.")]);

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Text));
    }

    private static string? GetConfiguredPath(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
    }
}
