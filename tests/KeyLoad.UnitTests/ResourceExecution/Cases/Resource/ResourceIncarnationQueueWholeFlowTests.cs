namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class ResourceIncarnationQueueWholeFlowTests
{
    [Test]
    public Task QueuePauseResumePreservesLifetimeAndQueuedWorkAcrossCorruptionRepairAndTwoColdOwners()
        => ResourceIncarnationOwner.RunAsync(fixture => ResourceIncarnationQueueFlow.RunAsync(
            fixture, TestContext.Current!.Execution.CancellationToken));
}
