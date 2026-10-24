namespace KeyLoad.UnitTests.Messaging;

internal sealed class SubscriptionLeaseVersionExhaustionTests
{
    [Test]
    public Task GenuinePendingMaximumLeaseVersionReturnsImmutableBoundedFailureThenColdExactRepairAndHealthyClaim()
        => SubscriptionLeaseVersionExhaustionFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
