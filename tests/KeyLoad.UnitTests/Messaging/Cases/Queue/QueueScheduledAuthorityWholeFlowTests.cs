namespace KeyLoad.UnitTests.Messaging;

internal sealed class QueueScheduledAuthorityWholeFlowTests
{
    [Test]
    public Task AcMsg004ScheduledMetadataMustMatchDueKeyBeforeColdRepairOriginalClaimProcessingAndHealthyWork()
        => QueueScheduledAuthorityTrial.RunAsync();
}
