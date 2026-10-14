namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkPendingBoundTests
{
    [Test]
    public async Task AcChunk008009012SameSeriesPendingRefusalReplaysThenActualMergeFreesHealthyCorrection()
        => await SampleChunkPendingBoundFlow.RunAsync();
}
