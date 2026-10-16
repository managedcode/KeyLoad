namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class UserAnalyticsHostLifecycleTests
{
    [Test]
    public Task ActualNativeAnalyticsGenerationsJoinBeforeSamePhysicalRootReopenAndReceiptReplay()
        => UserAnalyticsHostFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
