using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkSelectedIntegrityDamage
{
    internal static byte[] Key(ChunkSelectedIntegrityFixture fixture)
        => SampleChunkKeys.Manifest(fixture.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.FirstGeneration);

    internal static byte[] Inject(ChunkSelectedIntegrityFixture fixture, ChunkSelectedIntegrityFault fault)
    {
        var original = fixture.Store.Read(view => view.ReadOwnedValue(Key(fixture)))!;
        var envelope = OrleansSerialization.Deserialize<SampleChunkManifestEnvelope>(original);
        var manifest = OrleansSerialization.Deserialize<SampleChunkManifest>(envelope.Encoded.Span);
        var changed = fault switch
        {
            ChunkSelectedIntegrityFault.WindowIdentity => manifest with { WindowId = Guid.NewGuid() },
            ChunkSelectedIntegrityFault.WindowStart => manifest with { FromUtcTicks = manifest.FromUtcTicks + 1 },
            ChunkSelectedIntegrityFault.SourceSequence => manifest with { SourceSequence = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        var bytes = OrleansSerialization.Serialize(changed);
        Put(fixture, OrleansSerialization.Serialize(new SampleChunkManifestEnvelope(bytes, SHA256.HashData(bytes))));
        return original;
    }

    internal static void Put(ChunkSelectedIntegrityFixture fixture, byte[] bytes)
        => fixture.Store.Commit((tx, _) => { tx.Put(Key(fixture), bytes); return true; });

    internal static async Task Refuse(ChunkSelectedIntegrityFixture fixture)
    {
        var before = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var read = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Owner.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, fixture.WindowId,
                null, null, SampleChunkCanonicalFixture.OutputLimit)));
        await Assert.That(read.Code).IsEqualTo(ErrorCode.Corruption);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
        var apply = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Owner.Apply(fixture.Merge));
        await Assert.That(apply.Code).IsEqualTo(ErrorCode.Corruption);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
        await Assert.That(fixture.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            fixture.Partition, SampleChunkCanonicalFixture.Principal, fixture.Merge.CommandId)))).IsNull();
        await ChunkSelectedIntegrityAssertions.Raw(fixture, final: false);
    }
}
