namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class HostedRetainedOperationReadTests
{
    [Test]
    public Task OriginalSixtyFiveEntryJournalPreservesLateOperationAndWholeCutAcrossReadAndReopen()
        => HostedRetainedOperationReadFlow.ReadAndReopenAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OriginalRetainedJournalRequiresCompleteRecordAndAggregateByteBudgets(bool bytes)
        => HostedRetainedOperationReadFlow.BudgetAsync(bytes, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task EarlyCommandMatchCannotHideActualMissingLateNativeEntryAndRepairRemainsHealthy()
        => HostedRetainedOperationReadFlow.MissingAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task CanceledOriginalCallerCannotReleaseAnotherProtocolOwnerAndHealthyReadStillSucceeds()
        => HostedRetainedOperationReadFlow.CanceledAsync(TestContext.Current!.Execution.CancellationToken);
}
