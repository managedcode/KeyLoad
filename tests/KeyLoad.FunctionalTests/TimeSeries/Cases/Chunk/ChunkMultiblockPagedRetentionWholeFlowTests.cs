namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class ChunkMultiblockPagedRetentionWholeFlowTests
{
    [Test]
    public Task BoundedExpiryPagesHideFirstBlockPreserveSecondBlockThenRepairReplayAndTwoColdOwners()
        => ChunkMultiblockPagedRetentionFlow.RunAsync();
}
