namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class ChunkMultiblockReadBudgetWholeFlowTests
{
    [Test]
    public async Task SelectedSecondBlockCumulativeBytesRefuseThenExactRepairReplayAndTwoColdOwners()
        => await ChunkMultiblockReadBudgetFlow.RunAsync();
}
