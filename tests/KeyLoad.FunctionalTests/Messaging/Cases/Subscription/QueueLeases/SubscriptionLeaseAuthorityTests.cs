namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class SubscriptionLeaseAuthorityTests
{
    [Test]
    [Arguments(DeliveryAction.Ack)]
    [Arguments(DeliveryAction.Renew)]
    [Arguments(DeliveryAction.Nack)]
    public Task AcMsg089ForeignLeasedSlotRefusesOriginalClaimAndActionThenSameGuidRepairTwoColdHealthyAdvance(
        DeliveryAction action)
        => SubscriptionLeaseAuthorityFlow.RunAsync(action, TestContext.Current!.Execution.CancellationToken);
    [Test]
    [Arguments(DeliveryAction.Ack, SubscriptionDeliveryAuthorityFault.MissingWindow)]
    [Arguments(DeliveryAction.Renew, SubscriptionDeliveryAuthorityFault.MissingWindow)]
    [Arguments(DeliveryAction.Nack, SubscriptionDeliveryAuthorityFault.MissingWindow)]
    [Arguments(DeliveryAction.Ack, SubscriptionDeliveryAuthorityFault.MissingLeaseExpiry)]
    [Arguments(DeliveryAction.Renew, SubscriptionDeliveryAuthorityFault.MissingLeaseExpiry)]
    [Arguments(DeliveryAction.Nack, SubscriptionDeliveryAuthorityFault.MissingLeaseExpiry)]
    [Arguments(DeliveryAction.Ack, SubscriptionDeliveryAuthorityFault.UndefinedState)]
    [Arguments(DeliveryAction.Renew, SubscriptionDeliveryAuthorityFault.UndefinedState)]
    [Arguments(DeliveryAction.Nack, SubscriptionDeliveryAuthorityFault.UndefinedState)]
    public Task AcMsg089MalformedLeaseRefusesOriginalClaimAndActionThenSameGuidRepairTwoColdHealthyAdvance(
        DeliveryAction action, SubscriptionDeliveryAuthorityFault fault)
        => SubscriptionLeaseAuthorityFlow.RunAsync(action, TestContext.Current!.Execution.CancellationToken, fault);
}
