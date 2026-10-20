using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkPartialRetentionAssertions
{
    private const long SealedRevision = 5;
    private const long MergedRevision = 6;
    private const long FreshMergedRevision = 8;
    private const long OriginalGeneration = 1;
    private const long MergedGeneration = 2;
    private const long FreshGeneration = 3;
    private const long PurgedCount = 2;
    private const int BlockOrdinal = 0;
    private const string RawSampleFamily = "sample";

    internal static async Task Healthy(SampleChunkPartialRetentionFixture fixture, bool merged, bool fresh = false)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var floor = SampleChunkCanonicalFixture.Late.Timestamp;
        var late = new SampleRecord(SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Late,
            SampleChunkPartialRetentionFixture.LateSequence, SampleChunkPartialRetentionFixture.LateTags);
        var newer = new SampleRecord(SampleChunkCanonicalFixture.Series, SampleChunkPartialRetentionFixture.Fresh,
            SampleChunkPartialRetentionFixture.FreshSequence, SampleChunkCanonicalFixture.Tags);
        ImmutableArray<SampleRecord> visible = fresh ? [late, newer] : [late];
        var expected = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, fresh ? FreshGeneration : merged ? MergedGeneration : OriginalGeneration,
            fresh ? FreshMergedRevision : merged ? MergedRevision : SealedRevision,
            fresh ? SampleChunkPartialRetentionFixture.FreshSequence : SampleChunkPartialRetentionFixture.LateSequence,
            floor.UtcTicks, visible, fixture.Store.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(fixture.Read())))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        var raw = fixture.Owner.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Partition,
            SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.OutputLimit);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(raw)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(visible)));
        await RawRows(fixture, visible);
        await Assert.That(BitConverter.DoubleToInt64Bits(raw[BlockOrdinal].Sample.Value))
            .IsEqualTo(BitConverter.DoubleToInt64Bits(SampleChunkCanonicalFixture.Late.Value));
        var retention = fixture.Owner.ReadSampleRetention(SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series));
        await Assert.That(retention).IsEqualTo(new SampleRetentionStatus(floor, PurgedCount, false));
        var aggregate = fixture.Owner.AggregateSamples(SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.OutputLimit));
        await Assert.That(aggregate).IsEqualTo(fresh ? new SampleAggregate(2, 8, -0d, 8, 4) : new(1, 0, -0d, -0d, 0));
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
        if (merged)
        {
            await Representation(fixture, expected.Generation, fresh ? [.. SampleChunkCanonicalFixture.Expected(), late, newer] : [.. SampleChunkCanonicalFixture.Expected(), late]);
        }
    }

    private static async Task Representation(SampleChunkPartialRetentionFixture fixture, long generation,
        SampleRecord[] expected)
    {
        var block = Bytes(fixture, SampleChunkKeys.Block(fixture.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, generation, BlockOrdinal));
        var envelope = OrleansSerialization.Deserialize<SampleChunkManifestEnvelope>(Bytes(fixture,
            SampleChunkKeys.Manifest(fixture.Partition, SampleChunkCanonicalFixture.Set,
                SampleChunkCanonicalFixture.Series, fixture.WindowId, generation)));
        await Assert.That(envelope.Digest.Span.SequenceEqual(SHA256.HashData(envelope.Encoded.Span))).IsTrue();
        var manifest = OrleansSerialization.Deserialize<SampleChunkManifest>(envelope.Encoded.Span);
        await Assert.That(manifest.RecordCount).IsEqualTo(expected.Length);
        await Assert.That(manifest.BlockDigests.Length).IsEqualTo(1);
        await Assert.That(manifest.BlockDigests[BlockOrdinal].Span.SequenceEqual(SHA256.HashData(block))).IsTrue();
        await Assert.That(manifest.TagJsonValues.SequenceEqual(new[]
            { SampleChunkCanonicalFixture.Tags, SampleChunkPartialRetentionFixture.LateTags })).IsTrue();
        var expectedManifest = new SampleChunkManifest(SampleChunkLifecycleProtocol.Version, fixture.WindowId,
            generation, SampleChunkCanonicalFixture.Start.UtcTicks, SampleChunkCanonicalFixture.Until.UtcTicks,
            expected[^1].Sequence, expected.Length, [SHA256.HashData(block)],
            [SampleChunkCanonicalFixture.Tags, SampleChunkPartialRetentionFixture.LateTags],
            SampleChunkCanonicalFixture.Late.Timestamp.UtcTicks);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(manifest)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expectedManifest)));
        var decoded = SampleChunkCodec.DecodeOrdered(block, UnitExecutionOptions.TimeSeriesExecution(),
            SampleChunkCodec.MaximumEncodedBytes);
        await Assert.That(decoded.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(decoded[index])))
                .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected[index])));
            await Assert.That(BitConverter.DoubleToInt64Bits(decoded[index].Sample.Value))
                .IsEqualTo(BitConverter.DoubleToInt64Bits(expected[index].Sample.Value));
        }
    }

    internal static byte[] Bytes(SampleChunkPartialRetentionFixture fixture, byte[] key)
        => fixture.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original enrolled chunk row is missing.");

    private static async Task RawRows(SampleChunkPartialRetentionFixture fixture,
        ImmutableArray<SampleRecord> expected)
    {
        var page = fixture.Store.Read(view => view.Scan(SampleReadKeys.Prefix(fixture.Partition,
            SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series), SampleChunkCanonicalFixture.OutputLimit));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            var record = OrleansSerialization.Deserialize<SampleRecord>(page.Records[index].Value.Span);
            await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(record)))
                .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected[index])));
            var key = KeySpace.Partition(RawSampleFamily, fixture.Partition, SampleChunkCanonicalFixture.Set,
                SampleChunkCanonicalFixture.Series, expected[index].Sample.Timestamp, expected[index].Sequence);
            await Assert.That(page.Records[index].Key.Span.SequenceEqual(key)).IsTrue();
        }
    }

    internal static async Task Replay(SampleChunkPartialRetentionFixture fixture)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        foreach (var original in fixture.Outcomes)
        {
            var result = fixture.Owner.Apply(original.Operation);
            await Assert.That(SampleRollupWholeFlow.Outcome(result))
                .IsEqualTo(SampleRollupWholeFlow.Outcome(original.Result));
            await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
        }
    }
}
