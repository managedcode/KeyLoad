using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SubscriptionCheckpointAuthorityTests
{
    [Test]
    [Arguments(GroupDeliveryState.Acked)]
    [Arguments(GroupDeliveryState.Filtered)]
    public Task AcMsg089ForeignCompletedSlotRefusesContiguousAckThenExactRepairTwoColdReceiptsAndHealthyAdvance(
        GroupDeliveryState completedState)
        => SubscriptionCheckpointAuthorityFlow.RunAsync(completedState, TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(GroupDeliveryState.Acked)]
    [Arguments(GroupDeliveryState.Filtered)]
    public Task AcMsg089ForeignLaterCompletedSlotRefusesReceiveAcrossLeasedGapThenSameGuidRepairTwoColdHealthyAck(
        GroupDeliveryState completedState)
        => SubscriptionReceiveSlotAuthorityFlow.RunAsync(completedState, TestContext.Current!.Execution.CancellationToken);
}
