namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionAbsentTests
{
    private const string MissingPrincipalId = "missing-principal";

    [Test]
    public async Task AcCrs005MissingCommandAndPrincipalHaveNoNativeOutcome()
    {
        using var fixture = C1OutcomeInspectionFixture.Create();
        var missingCommand = Guid.NewGuid();
        await Assert.That(fixture.Database.Outcome(C1OutcomeInspectionAssertions.AdminId, missingCommand)).IsNull();
        await Assert.That(fixture.Database.Outcome(MissingPrincipalId, fixture.CommandId)).IsNull();
        await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: false, commandId: missingCommand);
        await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: false, principalId: MissingPrincipalId);
        await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture);
    }
}
