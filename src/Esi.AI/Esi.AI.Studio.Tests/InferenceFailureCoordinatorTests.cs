using Esi.AI.Core.ModelLoading;
using Esi.AI.Studio.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class InferenceFailureCoordinatorTests
{
    [TestMethod]
    public async Task FailAsync_WhenCalledTwice_StopsRuntimeAndRequestsShutdownOnce()
    {
        var runtime = new TestRuntime();
        var lifetime = new TestApplicationLifetime();
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var coordinator = new InferenceFailureCoordinator(
            runtime,
            lifetime,
            loggerFactory.CreateLogger<InferenceFailureCoordinator>());

        await coordinator.FailAsync(new InvalidOperationException("backend failure"));
        await coordinator.FailAsync(new InvalidOperationException("second backend failure"));

        Assert.AreEqual(1, runtime.StopCount);
        Assert.AreEqual(1, lifetime.StopApplicationCount);
    }

    [TestMethod]
    public async Task FailAsync_WhenRuntimeCleanupFails_StillRequestsShutdown()
    {
        var runtime = new TestRuntime(new InvalidOperationException("cleanup failure"));
        var lifetime = new TestApplicationLifetime();
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var coordinator = new InferenceFailureCoordinator(
            runtime,
            lifetime,
            loggerFactory.CreateLogger<InferenceFailureCoordinator>());

        await coordinator.FailAsync(new InvalidOperationException("backend failure"));

        Assert.AreEqual(1, runtime.StopCount);
        Assert.AreEqual(1, lifetime.StopApplicationCount);
    }

    private sealed class TestRuntime(Exception? failure = null) : IModelRuntimeShutdown
    {
        public int StopCount { get; private set; }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    private sealed class TestApplicationLifetime : IHostApplicationLifetime
    {
        public int StopApplicationCount { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => StopApplicationCount++;
    }
}