namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed class RequestWorkOwnerTests
{
    [Test]
    public Task AcCrsWork001EnforcesProducerAndTotalCapsAndExactLeaseRelease()
        => RequestWorkOwnerCases.AssertCapacityAndReleaseAsync();

    [Test]
    public Task AcCrsWork001ClosesAdmissionAndSharesTheOriginalDrain()
        => RequestWorkOwnerCases.AssertDrainAndClosedAdmissionAsync();

    [Test]
    public Task AcCrsWork001CancellationCallbackFailureDoesNotSkipOrDuplicateDrain()
        => RequestWorkOwnerCases.AssertCallbackFailureJoinsLiveLeaseAsync();
}
