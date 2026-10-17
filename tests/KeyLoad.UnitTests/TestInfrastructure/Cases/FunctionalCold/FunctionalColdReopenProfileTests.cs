namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class FunctionalColdReopenProfileTests
{
    [Test]
    [Arguments(1, false, false)]
    [Arguments(3, false, false)]
    [Arguments(1, true, true)]
    [Arguments(3, true, true)]
    [Arguments(0, false, true)]
    public Task ActualNativeModelKeepsFaultCommandsExclusiveToSelectedProfiles(int nodes, bool cold, bool faults)
        => FunctionalColdReopenProfileModel.VerifyAsync(nodes, cold, faults);

    [Test]
    public Task InvalidColdAndConflictingSelectionsRefuseBeforeResourcesThenHealthyModelWorks()
        => FunctionalColdReopenProfileRefusals.RunAsync();
}
