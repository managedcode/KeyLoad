namespace KeyLoad.UnitTests.Messaging;

internal sealed class CompetingInboxStateMachineTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task SeparatelyIdentifiedCompetingProcessingCommandsCommitOneProtectedTriadAndRetainBothOutcomesAcrossTwoColdOwners(bool equivalent)
        => CompetingInboxFlow.RunAsync(equivalent, TestContext.Current!.Execution.CancellationToken);
}
