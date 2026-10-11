namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class ChunkGenerationBoundTests
{
    [Test]
    public async Task AcChunk008010011SelectedGenerationCannotContainBaseSamplesNewerThanItsSourceWatermark()
        => await ChunkGenerationBoundTrial.Run();
}
