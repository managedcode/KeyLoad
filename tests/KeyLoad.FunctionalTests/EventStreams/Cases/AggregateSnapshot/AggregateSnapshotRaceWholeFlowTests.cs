namespace KeyLoad.FunctionalTests.EventStreams;

internal sealed class AggregateSnapshotRaceWholeFlowTests
{
    [Test]
    public Task CompetingSnapshotCasHasOneWholeWinnerStableLoserThenFreshPolicyRepairHealthyReplayAndTwoColdOwners()
        => AggregateSnapshotRaceFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
