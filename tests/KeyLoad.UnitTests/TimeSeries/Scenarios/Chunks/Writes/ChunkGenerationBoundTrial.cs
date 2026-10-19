using System.Security.Cryptography;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkGenerationBoundTrial
{
    internal static async Task Run()
    {
        using var fixture = new ChunkSelectedIntegrityFixture();
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: false);
        var originalCut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var original = await Inject(fixture);
        var damaged = SampleRawIntegrityAssertions.Capture(fixture.Store);
        await Assert.That(damaged.Position).IsEqualTo(originalCut.Position + 1);
        await ChunkSelectedIntegrityDamage.Refuse(fixture);
        await ChunkSelectedIntegrityDamage.Refuse(fixture);
        fixture.Reopen();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, damaged);
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
        var envelope = OrleansSerialization.Deserialize<SampleChunkManifestEnvelope>(original);
        var manifest = OrleansSerialization.Deserialize<SampleChunkManifest>(envelope.Encoded.Span);
        await Assert.That(manifest.SourceSequence).IsEqualTo(2);
        var bytes = OrleansSerialization.Serialize(manifest with
        {
            SourceSequence = 1
        });
        ChunkSelectedIntegrityDamage.Put(fixture,
            OrleansSerialization.Serialize(new SampleChunkManifestEnvelope(bytes, SHA256.HashData(bytes))));
        return original;
    }
}
