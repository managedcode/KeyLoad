namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class RevokedOutcomeAdmissionWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task HigherEpochAdmissionRefusalPreservesOriginalTriadReceiptLocatorAndNonexpiredCursorThenFreshRepairAndTwoColdOwners(bool revoked)
        => RevokedOutcomeAdmissionFlow.RunAsync(revoked, TestContext.Current!.Execution.CancellationToken);
}
