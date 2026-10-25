namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class ChunkManifestTagInventoryTests
{
    [Test]
    public Task AcChunk008010011012SelectedManifestMustDeclareActualImmutableBaseTagInventory()
        => ChunkManifestTagInventoryTrial.Run();
}
