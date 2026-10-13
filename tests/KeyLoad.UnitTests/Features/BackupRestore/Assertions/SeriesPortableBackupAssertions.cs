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
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Token.Position).IsEqualTo(database.Store.Position);
        await Assert.That(receipt.Durability).IsEqualTo(database.Store.Identity.Durability);
    }

    internal static async Task VisibleAsync(DatabaseEngine database, PartitionRef partition, bool complete, CancellationToken token)
    {
        var before = BackupPersistedPolicyAssertions.Capture(database.Store);
        SampleRecord[] expected = complete
            ? [new(SeriesPortableBackupSeed.Series, SeriesPortableBackupSeed.Data(3, 2, 3), 3L, "{}"),
                new(SeriesPortableBackupSeed.Series, SeriesPortableBackupSeed.Data(4, 3, 4), 4L, "{}"),
                new(SeriesPortableBackupSeed.Series, SeriesPortableBackupSeed.Data(5, 4, 5), 5L, "{}")]
            : [new(SeriesPortableBackupSeed.Series, SeriesPortableBackupSeed.Data(3, 2, 3), 3L, "{}"),
                new(SeriesPortableBackupSeed.Series, SeriesPortableBackupSeed.Data(4, 3, 4), 4L, "{}")];
        await BackupPersistedPolicyAssertions.SameAsync(expected, database.ReadSamples(SeriesPortableBackupSeed.Root,
            partition, SeriesPortableBackupSeed.Set, SeriesPortableBackupSeed.Series,
            SeriesPortableBackupSeed.Start, SeriesPortableBackupSeed.Start.AddSeconds(5), cancellationToken: token));
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
