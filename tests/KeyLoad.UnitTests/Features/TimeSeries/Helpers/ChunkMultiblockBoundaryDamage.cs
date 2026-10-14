using System.Security.Cryptography;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.TimeSeries;

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
        var payload = NativeSerialization.Deserialize<SampleChunkPayload>(block);
        var envelope = NativeSerialization.Deserialize<SampleChunkManifestEnvelope>(manifestBytes);
        var manifest = NativeSerialization.Deserialize<SampleChunkManifest>(envelope.Encoded.Span);
        await Assert.That(manifest.RecordCount).IsEqualTo(ChunkMultiblockBoundaryData.InitialCount);
        await Assert.That(manifest.BlockDigests.Length).IsEqualTo(ExpectedBlocks);
        await Assert.That(payload.RecordCount).IsEqualTo(SecondRecords);
        await Assert.That(payload.FormatVersion).IsEqualTo(SampleChunkWire.CurrentVersion);
        var first = fixture.Store.Read(view => view.ReadOwnedValue(SampleChunkKeys.Block(fixture.Partition,
            SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, fixture.WindowId, Generation, FirstOrdinal)))!;
        await Assert.That(NativeSerialization.Deserialize<SampleChunkPayload>(first).RecordCount).IsEqualTo(SampleChunkWire.MaximumRecords);
        var fault = new ChunkMultiblockBoundaryFault(blockKey, manifestKey, block, manifestBytes);
        return fault;
    }

    internal static void Inject(ChunkSelectedIntegrityFixture fixture, ChunkMultiblockBoundaryFault fault, bool unknownVersion)
    {
        if (!unknownVersion)
        {
            fixture.Store.Commit((tx, _) => { tx.Delete(fault.BlockKey); return true; });
            return;
        }
        var payload = NativeSerialization.Deserialize<SampleChunkPayload>(fault.BlockOriginal);
        var envelope = NativeSerialization.Deserialize<SampleChunkManifestEnvelope>(fault.ManifestOriginal);
        var manifest = NativeSerialization.Deserialize<SampleChunkManifest>(envelope.Encoded.Span);
        var changed = NativeSerialization.Serialize(payload with
        {
            FormatVersion = checked(SampleChunkWire.CurrentVersion + 1)
        });
        var updated = manifest with
        {
            BlockDigests = manifest.BlockDigests.SetItem(SecondOrdinal, SHA256.HashData(changed))
        };
        var encoded = NativeSerialization.Serialize(updated);
        fixture.Store.Commit((tx, _) =>
        {
            tx.Put(fault.BlockKey, changed);
            tx.PutRecord(fault.ManifestKey, new SampleChunkManifestEnvelope(encoded, SHA256.HashData(encoded)));
            return true;
        });
    }

    internal static void Repair(ChunkSelectedIntegrityFixture fixture, ChunkMultiblockBoundaryFault original)
        => fixture.Store.Commit((tx, _) =>
        {
            tx.Put(original.BlockKey, original.BlockOriginal);
            tx.Put(original.ManifestKey, original.ManifestOriginal);
            return true;
        });
}
