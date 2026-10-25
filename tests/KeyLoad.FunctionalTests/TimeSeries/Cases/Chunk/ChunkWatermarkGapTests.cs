namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class ChunkWatermarkGapTests
{
    [Test]
    public Task AcChunk007008010011OutsideWindowSourceGapsRemainHealthyAcrossMergeReplayAndTwoColdCuts()
        => ChunkWatermarkGapTrial.Run();
}
