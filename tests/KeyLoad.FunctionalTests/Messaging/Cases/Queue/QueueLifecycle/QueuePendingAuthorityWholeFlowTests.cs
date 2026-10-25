namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class QueuePendingAuthorityWholeFlowTests
{
    [Test]
    [Arguments(QueuePendingAuthorityFault.MissingMarker)]
    [Arguments(QueuePendingAuthorityFault.ForeignGeneration)]
    public Task AcMsg088SelectedPendingMarkerRefusesThenOriginalParkRepairReplayAndTwoColdOwnersPreserveInbox(
        QueuePendingAuthorityFault fault)
        => QueuePendingAuthorityTrial.RunAsync(fault);
}
