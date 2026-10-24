namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class UserAnalyticsHostLifecycleTests
{
    [Test]
    public Task ActualAnalyticsGenerationsJoinBeforeSamePhysicalRootReopenAndReceiptReplay()
        => UserAnalyticsHostFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
