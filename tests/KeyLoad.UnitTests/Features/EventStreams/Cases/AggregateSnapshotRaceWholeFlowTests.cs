namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateSnapshotRaceWholeFlowTests
{
    [Test]
    public Task CompetingSnapshotCasHasOneWholeWinnerStableLoserThenFreshPolicyRepairHealthyReplayAndTwoColdOwners()
        => AggregateSnapshotRaceFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
