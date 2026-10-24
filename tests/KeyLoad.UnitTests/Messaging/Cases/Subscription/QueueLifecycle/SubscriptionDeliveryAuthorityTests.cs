namespace KeyLoad.UnitTests.Messaging;

internal sealed class SubscriptionDeliveryAuthorityTests
{
    [Test]
    [Arguments(SubscriptionDeliveryAuthorityFault.MissingWindow)]
    [Arguments(SubscriptionDeliveryAuthorityFault.ForeignPosition)]
    [Arguments(SubscriptionDeliveryAuthorityFault.MissingLeaseExpiry)]
    [Arguments(SubscriptionDeliveryAuthorityFault.UndefinedState)]
    public Task AcMsg090CorruptLeaseCannotCommitEffectsInboxAckThenExactRepairColdAtomicHealthy(
        SubscriptionDeliveryAuthorityFault fault)
        => SubscriptionProcessingAtomicFlow.RunAsync(TestContext.Current!.Execution.CancellationToken, fault);
}
