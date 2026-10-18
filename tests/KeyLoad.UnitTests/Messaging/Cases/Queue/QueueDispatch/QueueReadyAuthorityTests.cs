namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueReadyAuthorityTests
{
    [Test]
    [Arguments(QueueReadyAuthorityFault.NegativeAttempts, false)]
    [Arguments(QueueReadyAuthorityFault.NegativeAttempts, true)]
    [Arguments(QueueReadyAuthorityFault.NonPositiveStateVersion, false)]
    [Arguments(QueueReadyAuthorityFault.NonPositiveStateVersion, true)]
    [Arguments(QueueReadyAuthorityFault.NegativeLeaseVersion, false)]
    [Arguments(QueueReadyAuthorityFault.NegativeLeaseVersion, true)]
    [Arguments(QueueReadyAuthorityFault.NonPositiveDeliveryGeneration, false)]
    [Arguments(QueueReadyAuthorityFault.NonPositiveDeliveryGeneration, true)]
    public Task NativeReadyNumericAuthorityRefusesOriginalClaimOrExpiryThenExactRepairTwoColdOwnersAndHealthyAck(
        QueueReadyAuthorityFault fault, bool expired)
        => QueueReadyAuthorityTrial.CounterAsync(fault, expired, TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(QueueReadyAuthorityFault.NegativeInFlightMessages, false)]
    [Arguments(QueueReadyAuthorityFault.NegativeInFlightMessages, true)]
    [Arguments(QueueReadyAuthorityFault.NegativeInFlightBytes, false)]
    [Arguments(QueueReadyAuthorityFault.NegativeInFlightBytes, true)]
    [Arguments(QueueReadyAuthorityFault.ExcessInFlightMessages, false)]
    [Arguments(QueueReadyAuthorityFault.ExcessInFlightMessages, true)]
    [Arguments(QueueReadyAuthorityFault.ExcessInFlightBytes, false)]
    [Arguments(QueueReadyAuthorityFault.ExcessInFlightBytes, true)]
    [Arguments(QueueReadyAuthorityFault.NegativeReadySequence, false)]
    [Arguments(QueueReadyAuthorityFault.NegativeReadySequence, true)]
    [Arguments(QueueReadyAuthorityFault.NegativeOrderSequence, false)]
    [Arguments(QueueReadyAuthorityFault.NegativeOrderSequence, true)]
    [Arguments(QueueReadyAuthorityFault.InFlightCountWithoutBytes, false)]
    [Arguments(QueueReadyAuthorityFault.InFlightCountWithoutBytes, true)]
    [Arguments(QueueReadyAuthorityFault.InFlightBytesWithoutCount, false)]
    [Arguments(QueueReadyAuthorityFault.InFlightBytesWithoutCount, true)]
    public Task NativeCounterAuthorityRefusesOriginalReceiveThenExactRepairTwoColdOwnersAndHealthyAck(
        QueueReadyAuthorityFault fault, bool expired)
        => QueueReadyAuthorityTrial.CounterAsync(fault, expired, TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(QueueReadyAuthorityFault.ScheduledState, false)]
    [Arguments(QueueReadyAuthorityFault.ScheduledState, true)]
    [Arguments(QueueReadyAuthorityFault.ReadySequence, false)]
    [Arguments(QueueReadyAuthorityFault.ReadySequence, true)]
    [Arguments(QueueReadyAuthorityFault.MetadataIdentity, false)]
    [Arguments(QueueReadyAuthorityFault.MetadataIdentity, true)]
    [Arguments(QueueReadyAuthorityFault.BodyIdentity, false)]
    [Arguments(QueueReadyAuthorityFault.BodyIdentity, true)]
    public Task NativeReadyAuthorityFaultPreservesOriginalInputThenRepairsClaimOrExpiryAndHealthyProcessing(
        QueueReadyAuthorityFault fault, bool expired)
        => QueueReadyAuthorityTrial.RunAsync(fault, expired, TestContext.Current!.Execution.CancellationToken);
}
