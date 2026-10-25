namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class QueueRetainedInboxAuthorityWholeFlowTests
{
    [Test]
    public Task AcMsg002RetainedInboxBinaryRefusalRepairEquivalentReceiptAndNewExecutionFenceSurviveTwoColdOwners()
        => QueueRetainedInboxAuthorityTrial.RunAsync();
}
