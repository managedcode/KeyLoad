namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionPresentTests
{
    [Test]
    public async Task AcCrs005RealCommittedNativeOutcomeIsObservedByGuardedChild()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: true);
            await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture);
        }).ConfigureAwait(false);
    }
}
