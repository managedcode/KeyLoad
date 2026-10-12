namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueReadyAuthorityTests
{
    [Test]
    [Arguments(QueueReadyAuthorityFault.ScheduledState, false)]
    [Arguments(QueueReadyAuthorityFault.ScheduledState, true)]
    [Arguments(QueueReadyAuthorityFault.ReadySequence, false)]
    [Arguments(QueueReadyAuthorityFault.ReadySequence, true)]
    [Arguments(QueueReadyAuthorityFault.MetadataIdentity, false)]
    [Arguments(QueueReadyAuthorityFault.MetadataIdentity, true)]
    [Arguments(QueueReadyAuthorityFault.BodyIdentity, false)]
    [Arguments(QueueReadyAuthorityFault.BodyIdentity, true)]
    public Task NativeReadyAuthorityFaultPreservesOriginalCommittedEntryThenRecoversClaimOrExpiryAndHealthyProcessing(
        QueueReadyAuthorityFault fault, bool expired)
        => QueueReadyAuthorityTrial.RunAsync(fault, expired, TestContext.Current!.Execution.CancellationToken);
}
