namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class ChunkSelectedBlockIntegrityTests
{
    [Test]
    [Arguments(SampleChunkCorruptionCut.MissingBlock)]
    [Arguments(SampleChunkCorruptionCut.BlockDigest)]
    public Task AcChunk008010011012SelectedImmutableBlockRefusesThenExactRepairReplayAndTwoColdCuts(
        SampleChunkCorruptionCut fault)
        => ChunkSelectedBlockIntegrityTrial.Run(fault);
}
