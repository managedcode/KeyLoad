using System.Buffers.Binary;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Verifies byte-exact ZoneTree operations against the immutable fixture corpus.</summary>
[NotInParallel]
internal sealed class RawStorageCrudTests
{
    private const int RecordCount = 64;
    private const int WriteBudget = 128;
    [Test]
    [Arguments(32)]
    [Arguments(1024)]
    public async Task AcGe002ZoneTreePreservesBinaryCrudAtBothPayloadSizes(int valueBytes)
    {
        using var fixture = new RawStorageFixture(RecordCount, valueBytes, WriteBudget);
        var originals = SnapshotCorpus(fixture.Corpus);
        await AssertCorpusMatchesIndependentVectorAsync(fixture.Corpus, originals, valueBytes);
        await Assert.That(fixture.Corpus.RecordCount).IsEqualTo(RecordCount);
        await Assert.That(fixture.Corpus.ValueBytes).IsEqualTo(valueBytes);
        for (var index = 0; index < RecordCount; index++)
        {
            await AssertRecordAsync(fixture, originals, index, alternate: false);
        }

        await Assert.That(fixture.TryRead(RecordCount, out _)).IsFalse();
        await Assert.That(fixture.TryRead(RecordCount + 1, out _)).IsFalse();

        fixture.Upsert(0, alternate: true);
        await AssertRecordAsync(fixture, originals, 0, alternate: true);
        fixture.Upsert(RecordCount - 1, alternate: true);
        await AssertRecordAsync(fixture, originals, RecordCount - 1, alternate: true);

        await Assert.That(fixture.Delete(0)).IsTrue();
        await Assert.That(fixture.TryRead(0, out _)).IsFalse();
        await Assert.That(fixture.Delete(0)).IsFalse();
        fixture.Upsert(0);
        await AssertRecordAsync(fixture, originals, 0, alternate: false);

        await Assert.That(fixture.Delete(RecordCount - 1)).IsTrue();
        await Assert.That(fixture.TryRead(RecordCount - 1, out _)).IsFalse();
        await Assert.That(fixture.Delete(RecordCount - 1)).IsFalse();
        fixture.Upsert(RecordCount - 1);
        await AssertRecordAsync(fixture, originals, RecordCount - 1, alternate: false);
        await AssertCorpusUnchangedAsync(fixture.Corpus, originals);
    }

    [Test]
    public async Task AcGe002ZoneTreeStartsWithEverySeededRecordAndIndependentBinaryBytes()
    {
        using var fixture = new RawStorageFixture(RecordCount, 32, WriteBudget);
        var originals = SnapshotCorpus(fixture.Corpus);
        for (var index = 0; index < RecordCount; index++)
        {
            await AssertRecordAsync(fixture, originals, index, alternate: false);
        }

        await AssertCorpusUnchangedAsync(fixture.Corpus, originals);
    }

    private static async Task AssertRecordAsync(RawStorageFixture fixture, CorpusSnapshot[] originals, int index,
        bool alternate)
    {
        var found = fixture.TryRead(index, out var actual);
        await Assert.That(found).IsTrue();
        var expected = alternate ? originals[index].AlternateValue : originals[index].OriginalValue;
        await Assert.That(actual.ToArray().AsSpan().SequenceEqual(expected)).IsTrue();
    }

    private static CorpusSnapshot[] SnapshotCorpus(RawStorageCorpus corpus)
    {
        var snapshots = new CorpusSnapshot[corpus.RecordCount];
        for (var index = 0; index < snapshots.Length; index++)
        {
            snapshots[index] = new(corpus.Key(index).ToArray(), corpus.Value(index, alternate: false).ToArray(),
                corpus.Value(index, alternate: true).ToArray());
        }

        return snapshots;
    }

    private static async Task AssertCorpusMatchesIndependentVectorAsync(RawStorageCorpus corpus,
        CorpusSnapshot[] snapshots, int valueBytes)
    {
        const ulong Seed = 1729;
        await Assert.That(snapshots.Length).IsEqualTo(RecordCount);
        for (var index = 0; index < snapshots.Length; index++)
        {
            var key = snapshots[index].Key;
            var expectedKey = new byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(expectedKey.AsSpan(0, sizeof(ulong)), Seed);
            BinaryPrimitives.WriteUInt64LittleEndian(expectedKey.AsSpan(sizeof(ulong), sizeof(ulong)), (ulong)index);
            await Assert.That(key.AsSpan().SequenceEqual(expectedKey)).IsTrue();
            await AssertValueVectorAsync(snapshots[index].OriginalValue, index, valueBytes, alternate: false);
            await AssertValueVectorAsync(snapshots[index].AlternateValue, index, valueBytes, alternate: true);
            await Assert.That(snapshots[index].OriginalValue.AsSpan()
                .SequenceEqual(snapshots[index].AlternateValue)).IsFalse();
        }

        await AssertCorpusUnchangedAsync(corpus, snapshots);
    }

    private static async Task AssertValueVectorAsync(byte[] actual, int index, int valueBytes, bool alternate)
    {
        const int Seed = 1729;
        var expected = new byte[valueBytes];
        var alternateSalt = alternate ? 13 : 0;
        expected[0] = unchecked((byte)(Seed + index + alternateSalt));
        expected[1] = alternate ? (byte)0xA5 : (byte)0x5A;
        expected[2] = 0;
        expected[3] = byte.MaxValue;
        for (var offset = 4; offset < expected.Length; offset++)
        {
            expected[offset] = unchecked((byte)(Seed + index * 31 + offset * 17 + alternateSalt));
        }

        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    private static async Task AssertCorpusUnchangedAsync(RawStorageCorpus corpus, CorpusSnapshot[] originals)
    {
        for (var index = 0; index < originals.Length; index++)
        {
            await Assert.That(corpus.Key(index).Span.SequenceEqual(originals[index].Key)).IsTrue();
            await Assert.That(corpus.Value(index).Span.SequenceEqual(originals[index].OriginalValue)).IsTrue();
            await Assert.That(corpus.Value(index, alternate: true).Span.SequenceEqual(originals[index].AlternateValue)).IsTrue();
        }
    }

    private sealed record CorpusSnapshot(byte[] Key, byte[] OriginalValue, byte[] AlternateValue);
}
