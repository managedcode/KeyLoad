namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed class ProjectionConsumerIdentityWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ForeignCommittedConsumerRefRefusesOriginalBatchAndRetentionAuthorityThenExactRepairEffectsContiguousCheckpointAndReceiptsSurviveTwoColdCuts(bool foreignPartition)
        => ProjectionConsumerIdentityFlow.RunAsync(foreignPartition);
}
