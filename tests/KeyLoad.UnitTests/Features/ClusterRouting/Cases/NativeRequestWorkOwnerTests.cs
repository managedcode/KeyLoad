namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeRequestWorkOwnerTests
{
    [Test]
    public Task AcCrsWork001EnforcesProducerAndTotalCapsAndExactLeaseRelease()
        => NativeRequestWorkOwnerCases.AssertCapacityAndReleaseAsync();

    [Test]
    public Task AcCrsWork001ClosesAdmissionAndSharesTheOriginalDrain()
        => NativeRequestWorkOwnerCases.AssertDrainAndClosedAdmissionAsync();

    [Test]
    public Task AcCrsWork001CancellationCallbackFailureDoesNotSkipOrDuplicateDrain()
        => NativeRequestWorkOwnerCases.AssertCallbackFailureJoinsLiveLeaseAsync();
}
