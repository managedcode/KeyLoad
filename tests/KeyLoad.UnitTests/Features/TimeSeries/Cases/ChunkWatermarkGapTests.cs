namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class ChunkWatermarkGapTests
{
    [Test]
    public Task AcChunk007008010011OutsideWindowSourceGapsRemainHealthyAcrossMergeReplayAndTwoColdCuts()
        => ChunkWatermarkGapTrial.Run();
}
