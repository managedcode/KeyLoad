namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueuePositiveInspectionAuthorityWholeFlowTests
{
    [Test]
    [Arguments(false, true)]
    [Arguments(false, false)]
    [Arguments(true, true)]
    [Arguments(true, false)]
    public Task AcMsg001ReadyScheduledInspectionRefusesSelectedBodyThenRepairOriginalProcessingAndHealthyTwoCold(
        bool scheduled, bool missingBody)
        => QueuePositiveInspectionAuthorityTrial.RunAsync(scheduled, missingBody);
}
