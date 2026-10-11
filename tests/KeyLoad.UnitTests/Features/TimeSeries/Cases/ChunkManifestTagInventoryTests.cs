namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class ChunkManifestTagInventoryTests
{
    [Test]
    public Task AcChunk008010011012SelectedManifestMustDeclareActualImmutableBaseTagInventory()
        => ChunkManifestTagInventoryTrial.Run();
}
