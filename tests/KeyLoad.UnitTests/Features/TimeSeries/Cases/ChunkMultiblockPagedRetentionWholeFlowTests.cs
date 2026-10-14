namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class ChunkMultiblockPagedRetentionWholeFlowTests
{
    [Test]
    public Task BoundedExpiryPagesHideFirstBlockPreserveSecondBlockThenRepairReplayAndTwoColdOwners()
        => ChunkMultiblockPagedRetentionFlow.RunAsync();
}
