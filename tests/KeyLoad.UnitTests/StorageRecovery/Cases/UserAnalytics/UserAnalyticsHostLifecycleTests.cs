namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class UserAnalyticsHostLifecycleTests
{
    [Test]
    public Task ActualAnalyticsGenerationsJoinBeforeSamePhysicalRootReopenAndReceiptReplay()
        => UserAnalyticsHostFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
