namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionPresentTests
{
    [Test]
    public async Task AcCrs005RealCommittedNativeOutcomeIsObservedByGuardedChild()
    {
        using var fixture = C1OutcomeInspectionFixture.Create();
        await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: true);
        await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture);
    }
}
