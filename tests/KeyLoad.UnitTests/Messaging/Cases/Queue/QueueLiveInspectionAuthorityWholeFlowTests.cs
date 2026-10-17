namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueLiveInspectionAuthorityWholeFlowTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task AcMsg001LiveInspectionRefusesMissingOrForeignRetainedBodyThenOriginalProcessingAndHealthyTwoCold(bool missingBody)
        => QueueLiveInspectionAuthorityTrial.RunAsync(missingBody);
}
