namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed class HostedRetainedOperationReadTests
{
    [Test]
    public Task OriginalSixtyFiveOutcomesPreserveLateCommandAndWholeCutAcrossReadAndReopen()
        => HostedRetainedOperationReadFlow.ReadAndReopenAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task ChangedPreparedCommandRefusesReadWithoutEffectsAndExactRepairRemainsHealthy()
        => HostedRetainedOperationReadFlow.MissingAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task CanceledOriginalCallerPreservesWholeCutAndHealthyReadStillSucceeds()
        => HostedRetainedOperationReadFlow.CanceledAsync(TestContext.Current!.Execution.CancellationToken);
}
