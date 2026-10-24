namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class ChunkManifestCardinalityTests
{
    [Test]
    public Task AcChunk008010011012SelectedManifestCountMustEqualActualDecodedImmutableBaseRows()
        => ChunkManifestCardinalityTrial.Run();
}
