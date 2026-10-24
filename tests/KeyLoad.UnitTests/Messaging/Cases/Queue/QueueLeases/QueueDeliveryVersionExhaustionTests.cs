namespace KeyLoad.UnitTests.Messaging;

internal sealed class QueueDeliveryVersionExhaustionTests
{
    [Test]
    [Arguments(DeliveryAction.Ack)]
    [Arguments(DeliveryAction.Nack)]
    [Arguments(DeliveryAction.Renew)]
    public Task ValidMaximumLeaseRefusesSettlementWithoutWrappingThenColdFixtureRepairPreservesFailedIdentityAndFreshHealthyWork(
        DeliveryAction action)
        => QueueDeliveryVersionExhaustionTrial.RunAsync(action, TestContext.Current!.Execution.CancellationToken);
}
