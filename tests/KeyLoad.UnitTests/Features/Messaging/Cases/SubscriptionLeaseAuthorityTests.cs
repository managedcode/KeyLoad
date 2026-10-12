namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SubscriptionLeaseAuthorityTests
{
    [Test]
    [Arguments(DeliveryAction.Ack)]
    [Arguments(DeliveryAction.Renew)]
    [Arguments(DeliveryAction.Nack)]
    public Task AcMsg089ForeignLeasedSlotRefusesOriginalClaimAndActionThenSameGuidRepairTwoColdHealthyAdvance(
        DeliveryAction action)
        => SubscriptionLeaseAuthorityFlow.RunAsync(action, TestContext.Current!.Execution.CancellationToken);
}
