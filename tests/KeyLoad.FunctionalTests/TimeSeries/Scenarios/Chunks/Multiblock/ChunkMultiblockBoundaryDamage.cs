using KeyLoad.Core.TimeSeries;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockBoundaryDamage
{
    private const long Generation = 2;
    private const int SecondOrdinal = 1;
    private const int FirstOrdinal = 0;
    private const int ExpectedBlocks = 2;
    private const int SecondRecords = 1;

    internal static async Task<ChunkMultiblockBoundaryFault> CaptureAsync(ChunkSelectedIntegrityFixture fixture)
    {
        var blockKey = SampleChunkKeys.Block(fixture.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, Generation, SecondOrdinal);
        var manifestKey = SampleChunkKeys.Manifest(fixture.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, Generation);
        var block = fixture.Store.Read(view => view.ReadOwnedValue(blockKey))!;
        var manifestBytes = fixture.Store.Read(view => view.ReadOwnedValue(manifestKey))!;
        var payload = OrleansSerialization.Deserialize<SampleChunkPayload>(block);
        var envelope = OrleansSerialization.Deserialize<SampleChunkManifestEnvelope>(manifestBytes);
        var manifest = OrleansSerialization.Deserialize<SampleChunkManifest>(envelope.Encoded.Span);
        await Assert.That(manifest.RecordCount).IsEqualTo(ChunkMultiblockBoundaryData.InitialCount);
        await Assert.That(manifest.BlockDigests.Length).IsEqualTo(ExpectedBlocks);
        await Assert.That(payload.RecordCount).IsEqualTo(SecondRecords);
        var first = fixture.Store.Read(view => view.ReadOwnedValue(SampleChunkKeys.Block(fixture.Partition,
            SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, fixture.WindowId, Generation, FirstOrdinal)))!;
        await Assert.That(OrleansSerialization.Deserialize<SampleChunkPayload>(first).RecordCount).IsEqualTo(SampleChunkWire.MaximumRecords);
        var fault = new ChunkMultiblockBoundaryFault(blockKey, block);
        return fault;
    }

    internal static void Inject(ChunkSelectedIntegrityFixture fixture, ChunkMultiblockBoundaryFault fault)
        => fixture.Store.Commit((tx, _) => { tx.Delete(fault.BlockKey); return true; });

    internal static void Repair(ChunkSelectedIntegrityFixture fixture, ChunkMultiblockBoundaryFault original)
        => fixture.Store.Commit((tx, _) =>
        {
            tx.Put(original.BlockKey, original.BlockOriginal);
            return true;
        });
}
