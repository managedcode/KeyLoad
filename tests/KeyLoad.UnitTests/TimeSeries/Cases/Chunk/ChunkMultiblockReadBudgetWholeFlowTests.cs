namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class ChunkMultiblockReadBudgetWholeFlowTests
{
    [Test]
    public async Task SelectedSecondBlockCumulativeBytesRefuseThenExactRepairReplayAndTwoColdOwners()
        => await ChunkMultiblockReadBudgetFlow.RunAsync();
}
