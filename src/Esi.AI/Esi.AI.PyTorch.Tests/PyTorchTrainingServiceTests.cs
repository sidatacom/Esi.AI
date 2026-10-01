using System.Collections.Concurrent;
using System.Diagnostics;
using Esi.AI.Models;
using Esi.AI.PyTorch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.PyTorch.Tests;

[TestClass]
public sealed class PyTorchTrainingServiceTests
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"esi-pytorch-tests-{Guid.NewGuid():N}");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(temporaryDirectory))
            Directory.Delete(temporaryDirectory, recursive: true);
    }

    [TestMethod]
    public void BuildStartInfo_DatasetPathContainsSpaces_PreservesArgumentBoundary()
    {
        var request = new CreateTrainingRunRequest { DatasetPath = "/tmp/data files/train set.jsonl" };

        var startInfo = PyTorchTrainingService.BuildStartInfo(request, "/tmp/run output", "/tmp/trainer script.py", "python3");

        CollectionAssert.Contains(startInfo.ArgumentList, request.DatasetPath);
        CollectionAssert.Contains(startInfo.ArgumentList, PyTorchTrainingService.ModelId);
        Assert.IsFalse(startInfo.UseShellExecute);
    }

    [TestMethod]
    public async Task TrainingRun_CreateAsync_ValidRequest_PublishesCompletedRun()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var datasetPath = Path.Combine(temporaryDirectory, "sample.jsonl");
        var scriptPath = Path.Combine(temporaryDirectory, "trainer.py");
        await File.WriteAllTextAsync(datasetPath, "{}\n");
        await File.WriteAllTextAsync(scriptPath, string.Empty);

        var publisher = new TestPublisher();
        var runner = new FakeProcessRunner(async (output, _) =>
        {
            await output("{\"type\":\"progress\",\"step\":1,\"total_steps\":1,\"loss\":0.42}");
            return 0;
        });
        await using var service = CreateService(publisher, runner, scriptPath);

        var pending = await service.TrainingRun_CreateAsync(new CreateTrainingRunRequest { DatasetPath = datasetPath });
        var completed = await publisher.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var runs = await service.TrainingRun_ReadAsync();

        Assert.AreEqual(TrainingRunState.Pending, pending.State);
        Assert.AreEqual(TrainingRunState.Completed, completed.State);
        Assert.AreEqual(1, completed.CurrentStep);
        Assert.AreEqual(0.42, completed.Loss);
        Assert.AreEqual(1, runs.Count);
        Assert.AreEqual(TrainingRunState.Completed, runs[0].State);
    }

    [TestMethod]
    public async Task TrainingRun_CreateAsync_WhileAnotherRunIsActive_RejectsSecondRun()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var datasetPath = Path.Combine(temporaryDirectory, "sample.jsonl");
        var scriptPath = Path.Combine(temporaryDirectory, "trainer.py");
        await File.WriteAllTextAsync(datasetPath, "{}\n");
        await File.WriteAllTextAsync(scriptPath, string.Empty);

        var publisher = new TestPublisher();
        var runner = new FakeProcessRunner(async (_, cancellationToken) =>
        {
            runnerStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        });
        await using var service = CreateService(publisher, runner, scriptPath);
        var request = new CreateTrainingRunRequest { DatasetPath = datasetPath };

        var active = await service.TrainingRun_CreateAsync(request);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TrainingRun_CreateAsync(request));
        await service.TrainingRun_DeleteAsync(active.Id);
        var deleted = await publisher.Deleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("already active", exception.Message);
        Assert.AreEqual(TrainingRunState.Cancelled, deleted.State);
        Assert.AreEqual(0, (await service.TrainingRun_ReadAsync()).Count);
    }

    [TestMethod]
    public async Task TrainingRun_CreateAsync_WhenWorkerExitsWithError_PublishesFailureAndRemovesRun()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var datasetPath = Path.Combine(temporaryDirectory, "sample.jsonl");
        var scriptPath = Path.Combine(temporaryDirectory, "trainer.py");
        await File.WriteAllTextAsync(datasetPath, "{}\n");
        await File.WriteAllTextAsync(scriptPath, string.Empty);

        var publisher = new TestPublisher();
        var runner = new FakeProcessRunner((_, _) => Task.FromResult(1));
        await using var service = CreateService(publisher, runner, scriptPath);

        await service.TrainingRun_CreateAsync(new CreateTrainingRunRequest { DatasetPath = datasetPath });
        var failedUpdate = await publisher.Failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var deleted = await publisher.Deleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(TrainingRunState.Failed, failedUpdate.State);
        Assert.AreEqual(TrainingRunState.Failed, deleted.State);
        Assert.IsTrue(deleted.Error?.Contains("exited with code 1", StringComparison.Ordinal) == true);
        Assert.AreEqual(0, (await service.TrainingRun_ReadAsync()).Count);
    }

    private readonly TaskCompletionSource runnerStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private PyTorchTrainingService CreateService(TestPublisher publisher, FakeProcessRunner runner, string scriptPath) =>
        new(
            new PyTorchTrainingOptions
            {
                OutputDirectory = Path.Combine(temporaryDirectory, "runs"),
                TrainerScriptPath = scriptPath
            },
            publisher,
            runner);

    private sealed class FakeProcessRunner(Func<Func<string, Task>, CancellationToken, Task<int>> execute) : IPythonTrainingProcessRunner
    {
        public Task<int> RunAsync(ProcessStartInfo startInfo, Func<string, Task> outputHandler, CancellationToken cancellationToken) =>
            execute(outputHandler, cancellationToken);
    }

    private sealed class TestPublisher : ITrainingRunStatusPublisher
    {
        public TaskCompletionSource<TrainingRunStatus> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TrainingRunStatus> Deleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TrainingRunStatus> Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PublishCreateAsync(TrainingRunStatus status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PublishUpdateAsync(TrainingRunStatus status, CancellationToken cancellationToken = default)
        {
            if (status.State == TrainingRunState.Completed)
                Completed.TrySetResult(status);
            if (status.State == TrainingRunState.Failed)
                Failed.TrySetResult(status);
            return Task.CompletedTask;
        }

        public Task PublishDeleteAsync(TrainingRunStatus status, CancellationToken cancellationToken = default)
        {
            Deleted.TrySetResult(status);
            return Task.CompletedTask;
        }
    }
}