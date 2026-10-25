namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class SubscriptionGroupProgressAuthorityTests
{
    [Test]
    [Arguments(SubscriptionGroupProgressFault.NegativeCheckpoint)]
    [Arguments(SubscriptionGroupProgressFault.NegativeIssuedPosition)]
    [Arguments(SubscriptionGroupProgressFault.SignedOverflow)]
    [Arguments(SubscriptionGroupProgressFault.IssuedBeforeExistingRow)]
    [Arguments(SubscriptionGroupProgressFault.FutureCheckpoint)]
    [Arguments(SubscriptionGroupProgressFault.IssuedBeforeCheckpoint)]
    [Arguments(SubscriptionGroupProgressFault.IssuedBeyondTail)]
    [Arguments(SubscriptionGroupProgressFault.WindowExceeded)]
    public Task ImpossibleProgressRefusesReceiveAckAndStatusThenExactRepairColdReceiptsAndHealthyAdvance(
        SubscriptionGroupProgressFault fault)
        => SubscriptionGroupProgressAuthorityFlow.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
