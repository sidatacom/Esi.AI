using System.Runtime.CompilerServices;
using Esi.AI.Core.Chat;
using Esi.AI.Core.Grpc;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class PythonInferenceGrpcTests
{
    [TestMethod]
    public void ToGrpcRequest_LoadRequest_MapsBackendAndRuntimeOptions()
    {
        var request = new PythonInferenceLoadRequest(
            "Qwen/test",
            ConfigurationBackend.Vllm,
            Port: 7011,
            GpuMemoryUtilization: 75,
            MaxModelLength: 4096,
            TensorParallelSize: 2,
            TrustRemoteCode: false,
            EnforceEager: true);

        var grpcRequest = PythonInferenceGrpcMapper.ToGrpcRequest(request);

        Assert.AreEqual("Qwen/test", grpcRequest.ModelPath);
        Assert.AreEqual("vllm", grpcRequest.Engine);
        Assert.AreEqual((uint)4096, grpcRequest.MaxModelLen);
        Assert.AreEqual((uint)2, grpcRequest.TensorParallelSize);
        Assert.AreEqual(.75f, grpcRequest.GpuMemoryUtilization, .001f);
        Assert.IsFalse(grpcRequest.TrustRemoteCode);
        Assert.IsTrue(grpcRequest.EnforceEager);
    }

    [TestMethod]
    public void ToGrpcRequest_EmptyMessages_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(() => PythonInferenceGrpcMapper.ToGrpcRequest([], "local-model"));
    }

    [TestMethod]
    public async Task GenerateWithStatsAsync_FakeStream_ReturnsMappedTextAndStatistics()
    {
        GenerateRequest? capturedRequest = null;
        using var session = new PythonInferenceChatSession(
            (request, _) => CaptureAndStreamAsync(request, value => capturedRequest = value),
            () => "vLLM",
            () => "Qwen/test");

        var result = await session.GenerateWithStatsAsync([
            new LlamaChatMessage("system", "Be brief."),
            new LlamaChatMessage("user", "Hello")]);

        Assert.AreEqual("Hello world", result.Text);
        Assert.AreEqual(3, result.TokenCount);
        Assert.AreEqual(4.2d, result.TokensPerSecond, .001d);
        Assert.IsNotNull(capturedRequest);
        Assert.AreEqual("Qwen/test", capturedRequest.ModelId);
        Assert.AreEqual(2, capturedRequest.Messages.Count);
        Assert.AreEqual("system", capturedRequest.Messages[0].Role);
        Assert.AreEqual("Hello", capturedRequest.Messages[1].Content);
    }

    [TestMethod]
    public async Task GenerateWithStatsAsync_FakeErrorResponse_ThrowsInformativeException()
    {
        using var session = new PythonInferenceChatSession(
            (_, _) => ErrorStreamAsync(),
            () => "vLLM",
            () => "Qwen/test");

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => session.GenerateWithStatsAsync([new LlamaChatMessage("user", "Hello")]));

        StringAssert.Contains(exception.Message, "vLLM generation failed");
        StringAssert.Contains(exception.Message, "backend unavailable");
    }

    [TestMethod]
    public async Task GenerateWithStatsAsync_CancellationToken_CancelsFakeStream()
    {
        using var session = new PythonInferenceChatSession(
            (_, cancellationToken) => CancellationStreamAsync(cancellationToken),
            () => "vLLM",
            () => "Qwen/test");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => session.GenerateWithStatsAsync([new LlamaChatMessage("user", "Hello")], cancellation.Token));
    }

    private static async IAsyncEnumerable<GenerateResponse> CaptureAndStreamAsync(
        GenerateRequest request,
        Action<GenerateRequest> capture,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        capture(request);
        await Task.Yield();
        yield return new GenerateResponse { Delta = "Hello", GeneratedTokens = 1 };
        yield return new GenerateResponse { Delta = " world", GeneratedTokens = 2 };
        yield return new GenerateResponse { Finished = true, GeneratedTokens = 3, TokensPerSecond = 4.2d };
    }

    private static async IAsyncEnumerable<GenerateResponse> ErrorStreamAsync()
    {
        await Task.Yield();
        yield return new GenerateResponse { Error = "backend unavailable" };
    }

    private static async IAsyncEnumerable<GenerateResponse> CancellationStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }
}