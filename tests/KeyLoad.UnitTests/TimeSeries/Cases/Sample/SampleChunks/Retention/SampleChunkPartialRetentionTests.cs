namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class SampleChunkPartialRetentionTests
{
    [Test]
    public async Task AcChunk008011012PartialFloorMergePreservesEncodedHistoryAndExactReplayAcrossTwoColdOwners()
        => await SampleChunkPartialRetentionFlow.RunAsync();
}
