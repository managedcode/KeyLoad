namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SubscriptionPendingCounterAuthorityTests
{
    [Test]
    [Arguments(SubscriptionPendingCounterFault.NegativeAttempts)]
    [Arguments(SubscriptionPendingCounterFault.NegativeLeaseVersion)]
    public Task GenuineNackPendingCountersCannotProduceNegativeLeaseThenExactRepairColdReceiptsAndHealthyProgress(
        SubscriptionPendingCounterFault fault)
        => SubscriptionPendingCounterAuthorityFlow.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
