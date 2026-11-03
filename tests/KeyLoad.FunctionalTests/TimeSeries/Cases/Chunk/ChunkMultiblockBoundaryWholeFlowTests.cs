namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class ChunkMultiblockBoundaryWholeFlowTests
{
    [Test]
    public Task Actual257RecordGenerationRefusesMissingSecondBlockThenExactRepairReplayAndTwoCold()
        => ChunkMultiblockBoundaryFlow.RunAsync();
}
