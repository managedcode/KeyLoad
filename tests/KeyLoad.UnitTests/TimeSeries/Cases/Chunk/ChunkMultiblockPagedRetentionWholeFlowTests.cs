namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class ChunkMultiblockPagedRetentionWholeFlowTests
{
    [Test]
    public Task BoundedExpiryPagesHideFirstBlockPreserveSecondBlockThenRepairReplayAndTwoColdOwners()
        => ChunkMultiblockPagedRetentionFlow.RunAsync();
}
