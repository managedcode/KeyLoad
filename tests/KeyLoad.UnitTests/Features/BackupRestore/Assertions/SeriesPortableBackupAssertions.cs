using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class SeriesPortableBackupAssertions
{
    private const int MaximumSampleRows = 8;

    internal static string[] PhysicalRows(DatabaseEngine database, PartitionRef partition)
        => database.Store.Read(view =>
        {
            var page = view.Scan(SampleReadKeys.Prefix(partition, SeriesPortableBackupSeed.Set,
                SeriesPortableBackupSeed.Series), MaximumSampleRows);
            if (page.HasMore)
            { throw new InvalidOperationException("The portable sample fixture exceeded its row bound."); }
            return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" +
                Convert.ToHexString(row.Value.Span)).ToArray();
        });

    internal static async Task ReceiptAsync(DatabaseEngine database, PartitionRef partition,
        ReplicatedOperation operation, OperationResult result)
    {
        await Assert.That(result.Error).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(operation.Id);
        var owner = database.Store.Read(view => DatabaseEngine.ReadPlacementWitness(view, partition));
        await BackupPersistedPolicyAssertions.SameAsync(new CommitToken(owner.Incarnation,
            partition.AtomicPartitionId, database.Store.Position, owner.PlacementEpoch), receipt.Token);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Token.Position).IsEqualTo(database.Store.Position);
        await Assert.That(receipt.Durability).IsEqualTo(database.Store.Identity.Durability);
    }

    private static async Task RequireRowsAsync(SampleRecord[] expected, SampleRecord[] actual)
    {
        for (var row = 1; row < expected.Length; row++)
        {
            await Assert.That(ReferenceEquals(expected[row].SeriesId, expected[0].SeriesId)).IsFalse();
            await Assert.That(ReferenceEquals(expected[row].TagsJson, expected[0].TagsJson)).IsFalse();
            await Assert.That(ReferenceEquals(actual[row].SeriesId, actual[0].SeriesId)).IsFalse();
            await Assert.That(ReferenceEquals(actual[row].TagsJson, actual[0].TagsJson)).IsFalse();
        }
        var expectedBytes = NativeSerialization.Serialize(expected);
        var actualBytes = NativeSerialization.Serialize(actual);
        await Assert.That(actualBytes.AsSpan().SequenceEqual(expectedBytes)).IsTrue()
            .Because(DescribeRows(expected, actual, expectedBytes, actualBytes));
    }

    private static string DescribeRows(SampleRecord[] expected, SampleRecord[] actual,
        byte[] expectedBytes, byte[] actualBytes)
    {
        var series = true;
        var events = true;
        var timestamps = true;
        var values = true;
        var sequences = true;
        var tags = true;
        var expectedSeriesAliases = true;
        var actualSeriesAliases = true;
        var expectedTagsAliases = true;
        var actualTagsAliases = true;
        for (var row = 0; row < Math.Min(expected.Length, actual.Length); row++)
        {
            var left = expected[row];
            var right = actual[row];
            series &= left.SeriesId == right.SeriesId;
            events &= left.Sample.EventId == right.Sample.EventId;
            timestamps &= left.Sample.Timestamp.EqualsExact(right.Sample.Timestamp);
            values &= BitConverter.DoubleToInt64Bits(left.Sample.Value) == BitConverter.DoubleToInt64Bits(right.Sample.Value);
            sequences &= left.Sequence == right.Sequence;
            tags &= left.TagsJson == right.TagsJson;
            expectedSeriesAliases &= ReferenceEquals(left.SeriesId, expected[0].SeriesId);
            actualSeriesAliases &= ReferenceEquals(right.SeriesId, actual[0].SeriesId);
            expectedTagsAliases &= ReferenceEquals(left.TagsJson, expected[0].TagsJson);
            actualTagsAliases &= ReferenceEquals(right.TagsJson, actual[0].TagsJson);
        }
        var offset = 0;
        while (offset < Math.Min(expectedBytes.Length, actualBytes.Length)
            && expectedBytes[offset] == actualBytes[offset])
        { offset++; }
        return $"Series count expected={expected.Length} actual={actual.Length}; bytes expected={expectedBytes.Length} actual={actualBytes.Length} firstDifference={offset}; "
            + $"series={series} event={events} timestampExact={timestamps} valueBits={values} sequence={sequences} tags={tags}; "
            + $"expectedSeriesAliases={expectedSeriesAliases} actualSeriesAliases={actualSeriesAliases} expectedTagsAliases={expectedTagsAliases} actualTagsAliases={actualTagsAliases}.";
    }

    private static SampleRecord ExpectedRow(int id, int seconds, double value, long sequence)
        => new(new string(SeriesPortableBackupSeed.Series.AsSpan()),
            SeriesPortableBackupSeed.Data(id, seconds, value), sequence, new string("{}".AsSpan()));

    internal static async Task VisibleAsync(DatabaseEngine database, PartitionRef partition, bool complete, CancellationToken token)
    {
        var before = BackupPersistedPolicyAssertions.Capture(database.Store);
        SampleRecord[] expected = complete
            ? [ExpectedRow(3, 2, 3d, 3L), ExpectedRow(4, 3, 4d, 4L), ExpectedRow(5, 4, 5d, 5L)]
            : [ExpectedRow(3, 2, 3d, 3L), ExpectedRow(4, 3, 4d, 4L)];
        var actual = database.ReadSamples(SeriesPortableBackupSeed.Root,
            partition, SeriesPortableBackupSeed.Set, SeriesPortableBackupSeed.Series,
            SeriesPortableBackupSeed.Start, SeriesPortableBackupSeed.Start.AddSeconds(5), cancellationToken: token);
        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await RequireRowsAsync(expected, actual);
        var expectedAggregate = complete ? new SampleAggregate(3L, 12d, 3d, 5d, 4d)
            : new SampleAggregate(2L, 7d, 3d, 4d, 3.5d);
        await BackupPersistedPolicyAssertions.SameAsync(expectedAggregate,
            database.AggregateSamples(SeriesPortableBackupSeed.Root,
                new(partition, SeriesPortableBackupSeed.Set, SeriesPortableBackupSeed.Series,
                    SeriesPortableBackupSeed.Start, SeriesPortableBackupSeed.Start.AddSeconds(5), MaximumSampleRows), token));
        var status = database.ReadSampleRetention(SeriesPortableBackupSeed.Root,
            new(partition, SeriesPortableBackupSeed.Set, SeriesPortableBackupSeed.Series), token);
        await BackupPersistedPolicyAssertions.SameAsync(new SampleRetentionStatus(SeriesPortableBackupSeed.Floor,
            complete ? 2L : 1L, !complete), status);
        await Assert.That(PhysicalRows(database, partition).Length).IsEqualTo(3);
        await BackupPersistedPolicyAssertions.UnchangedAsync(database.Store, before);
    }

    internal static async Task ReplayAsync(DatabaseEngine database,
        (ReplicatedOperation Operation, OperationResult Result)[] operations, CancellationToken token)
    {
        foreach (var original in operations)
        { await BackupPersistedPolicyAssertions.ReplayAsync(database, original.Operation, original.Result, token); }
    }

    internal static async Task SourceOutcomesAsync(BackupIndexedDocumentDedupFixture fixture, byte[][] originals,
        (ReplicatedOperation Operation, OperationResult Result)[] operations)
    {
        for (var index = 0; index < operations.Length; index++)
        {
            await Assert.That(BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.TargetStore,
                fixture.Partition, SeriesPortableBackupSeed.Root, operations[index].Operation.Id))
                .IsEquivalentTo(originals[index], CollectionOrdering.Matching);
        }
    }
}
