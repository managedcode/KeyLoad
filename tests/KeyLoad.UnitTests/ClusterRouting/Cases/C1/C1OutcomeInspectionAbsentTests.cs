using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionAbsentTests
{
    private const string MissingPrincipalId = "missing-principal";

    [Test]
    public async Task AcCrs005MissingCommandAndPrincipalHaveNoNativeOutcome()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var missingCommand = Guid.NewGuid();
            await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(
                fixture.Partition, C1OutcomeInspectionAssertions.AdminId, missingCommand)))).IsNull();
            await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(
                fixture.Partition, C1OutcomeInspectionAssertions.AdminId, missingCommand)))).IsNull();
            await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(
                fixture.Partition, MissingPrincipalId, fixture.CommandId)))).IsNull();
            await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(
                fixture.Partition, MissingPrincipalId, fixture.CommandId)))).IsNull();
            await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: false, commandId: missingCommand);
            await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: false, principalId: MissingPrincipalId);
            await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture);
        }).ConfigureAwait(false);
    }
}
