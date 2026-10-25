namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class RequestWorkOwnerTests
{
    [Test]
    public Task AcCrsWork001AdmitsUnboundedWorkAndRejectsOnlyADuplicateActiveIdentity()
        => RequestWorkOwnerCases.AssertUnlimitedWorkAndDuplicateIdentityAsync();

    [Test]
    public Task AcCrsWork001ClosesAdmissionAndSharesTheOriginalDrain()
        => RequestWorkOwnerCases.AssertDrainAndClosedAdmissionAsync();

    [Test]
    public Task AcCrsWork001CancellationCallbackFailureDoesNotSkipOrDuplicateDrain()
        => RequestWorkOwnerCases.AssertCallbackFailureJoinsLiveLeaseAsync();
}
