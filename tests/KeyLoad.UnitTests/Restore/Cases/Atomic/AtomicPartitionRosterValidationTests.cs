using KeyLoad.Core.BackupRestore.Execution;
using KeyLoad.Core.BackupRestore.Serialization;
using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class AtomicPartitionRosterValidationTests
{
    private const long FirstSeenIndex = 3;
    private const long AppliedIndexStep = 1;
    private const long NoStorePosition = 0;
    private const long NegativeAppliedIndex = -1;
    private const long FutureStorePositionStep = 2;
    private const int VersionStep = 1;
    private const string RootPrincipalId = "root";
    private const string DocumentId = "invalid-roster-document";
    private const string DocumentJson = "{\"value\":1}";
    private const byte MalformedKeyValue = 1;

    [Test]
    public async Task AcBackup004RejectsAllMalformedPersistedFirstSeenCoordinatesAndIdentity()
    {
        await AssertInvalidEntryAsync(position => Entry(position, FirstSeenIndex));
        await AssertInvalidEntryAsync(_ => Entry(NoStorePosition, FirstSeenIndex + AppliedIndexStep));
        await AssertInvalidEntryAsync(position => Entry(position, NoStorePosition,
            version: AtomicPartitionRosterProtocol.CurrentVersion + VersionStep));
        await AssertInvalidEntryAsync(position => Entry(position, NoStorePosition,
            partition: AtomicPartitionRosterFixture.Destination));
        await AssertInvalidEntryAsync(position => Entry(checked(position + FutureStorePositionStep), NoStorePosition));
        await AssertInvalidEntryAsync(_ => Entry(NoStorePosition, NoStorePosition));
        await AssertInvalidEntryAsync(_ => Entry(NoStorePosition, NegativeAppliedIndex));
    }

    [Test]
    public async Task AcBackup004MalformedRecognizedScopedKeyFailsAsCorruption()
    {
        using var fixture = new AtomicPartitionRosterFixture();
        var malformedKey = KeyCodec.Encode(PartitionRecordFamilies.Document);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Commit((transaction, position) =>
        {
            var roster = new AtomicPartitionRosterTransaction(transaction, position, NoStorePosition);
            roster.Put(malformedKey, [MalformedKeyValue]);
            return true;
        }));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(malformedKey))).IsNull();
    }

    private static AtomicPartitionCatalogEntry Entry(long storePosition, long appliedIndex,
        int version = AtomicPartitionRosterProtocol.CurrentVersion, PartitionRef? partition = null)
        => new(version, partition ?? AtomicPartitionRosterFixture.Source, storePosition, appliedIndex);

    private static async Task AssertInvalidEntryAsync(Func<long, AtomicPartitionCatalogEntry> createEntry)
    {
        using var fixture = new AtomicPartitionRosterFixture();
        fixture.ConfigureGraph();
        var partition = AtomicPartitionRosterFixture.Source;
        fixture.Store.Commit((transaction, position) =>
        {
            transaction.PutRecord(AtomicPartitionRosterKeys.Partition(partition), createEntry(position));
            return true;
        });
        var positionBeforeRejectedCommand = fixture.Store.Position;
        var originalEntryBytes = fixture.ReadEntryBytes(partition)!;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Batch(partition,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, DocumentJson)));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(fixture.Store.Position).IsEqualTo(positionBeforeRejectedCommand);
        await Assert.That(fixture.ReadEntryBytes(partition)!.AsSpan().SequenceEqual(originalEntryBytes)).IsTrue();
        await Assert.That(fixture.Database.GetDocument(RootPrincipalId,
            new(partition, AtomicPartitionRosterFixture.Collection, DocumentId))).IsNull();
    }
}
