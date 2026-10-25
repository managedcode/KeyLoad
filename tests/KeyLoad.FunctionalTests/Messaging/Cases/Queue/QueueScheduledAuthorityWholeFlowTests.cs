namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class QueueScheduledAuthorityWholeFlowTests
{
    [Test]
    public Task AcMsg004ScheduledMetadataMustMatchDueKeyBeforeColdRepairOriginalClaimProcessingAndHealthyWork()
        => QueueScheduledAuthorityTrial.RunAsync();
}
