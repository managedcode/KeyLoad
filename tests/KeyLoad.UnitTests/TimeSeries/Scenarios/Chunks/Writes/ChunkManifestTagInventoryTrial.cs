using System.Security.Cryptography;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkManifestTagInventoryTrial
{
    internal static async Task Run()
    {
        using var fixture = new ChunkSelectedIntegrityFixture();
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: false);
        var originalCut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var original = await Inject(fixture);
        var damaged = SampleRawIntegrityAssertions.Capture(fixture.Store);
        await ChunkManifestTagInventoryAssertions.OnlyOwnedRowChanged(fixture, originalCut, damaged);
        await ChunkSelectedIntegrityDamage.Refuse(fixture);
        await ChunkSelectedIntegrityDamage.Refuse(fixture);
        fixture.Reopen();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, damaged);
        await ChunkManifestTagInventoryAssertions.OnlyOwnedRowChanged(fixture, originalCut, damaged);
        await ChunkSelectedIntegrityDamage.Refuse(fixture);
        ChunkSelectedIntegrityDamage.Put(fixture, original);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, originalCut with
        {
            Position = originalCut.Position + 2
        });
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: false);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        fixture.Apply(fixture.Merge);
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: false, generation: 2, revision: 6);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        fixture.Apply(fixture.Operation(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [ChunkSelectedIntegrityFixture.Fresh], SampleChunkCanonicalFixture.Tags)));
        fixture.Apply(fixture.Operation(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, 7)));
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: true);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        var healthy = SampleRawIntegrityAssertions.Capture(fixture.Store);
        fixture.Reopen();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, healthy);
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: true);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, healthy);
    }

    private static async Task<byte[]> Inject(ChunkSelectedIntegrityFixture fixture)
    {
        var original = fixture.Store.Read(view => view.ReadOwnedValue(ChunkSelectedIntegrityDamage.Key(fixture)))!;
        var envelope = NativeSerialization.Deserialize<SampleChunkManifestEnvelope>(original);
        var manifest = NativeSerialization.Deserialize<SampleChunkManifest>(envelope.Encoded.Span);
        await Assert.That(manifest.TagJsonValues.Length).IsEqualTo(1);
        await Assert.That(manifest.TagJsonValues[0]).IsEqualTo(SampleChunkCanonicalFixture.Tags);
        var bytes = NativeSerialization.Serialize(manifest with
        {
            TagJsonValues = ["{\"visible\":18}"]
        });
        ChunkSelectedIntegrityDamage.Put(fixture,
            NativeSerialization.Serialize(new SampleChunkManifestEnvelope(bytes, SHA256.HashData(bytes))));
        return original;
    }
}
