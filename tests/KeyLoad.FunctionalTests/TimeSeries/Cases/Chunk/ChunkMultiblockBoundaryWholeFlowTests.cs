namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class ChunkMultiblockBoundaryWholeFlowTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task Actual257RecordGenerationRefusesSecondBlockThenExactRepairReplayAndTwoCold(bool unknownVersion)
        => ChunkMultiblockBoundaryFlow.RunAsync(unknownVersion);
}
