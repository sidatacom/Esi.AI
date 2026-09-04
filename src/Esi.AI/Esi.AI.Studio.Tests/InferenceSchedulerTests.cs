using Esi.AI.Studio.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class InferenceSchedulerTests
{
    [TestMethod]
    public async Task RunAsync_WhenTwoGenerationsOverlap_SerializesOperations()
    {
        using var scheduler = new InferenceScheduler();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = scheduler.RunAsync(async () =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task;
            return 1;
        });
        await firstEntered.Task;

        var second = scheduler.RunAsync(async () =>
        {
            secondEntered.SetResult();
            return 2;
        });

        var completedBeforeRelease = await Task.WhenAny(secondEntered.Task, Task.Delay(100));
        Assert.AreNotSame(secondEntered.Task, completedBeforeRelease);

        releaseFirst.SetResult();
        Assert.AreEqual(1, await first);
        Assert.AreEqual(2, await second);
    }
}
