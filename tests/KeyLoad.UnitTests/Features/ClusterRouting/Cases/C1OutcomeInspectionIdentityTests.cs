using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionIdentityTests
{
    [Test]
    public async Task AcCrs005WrongNodeOrIncarnationFailsClosed()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var wrongNode = await C1OutcomeInspectionAssertions.RunAsync(fixture, nodeId: Guid.NewGuid());
            await C1OutcomeInspectionAssertions.AssertRejectedAsync(wrongNode, C1OutcomeInspectionFailurePhase.OpenStore);
            var wrongIncarnation = await C1OutcomeInspectionAssertions.RunAsync(fixture, incarnation: Guid.NewGuid());
            await C1OutcomeInspectionAssertions.AssertRejectedAsync(wrongIncarnation, C1OutcomeInspectionFailurePhase.OpenStore);
            await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture);
            await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: true);
        }).ConfigureAwait(false);
    }
}
