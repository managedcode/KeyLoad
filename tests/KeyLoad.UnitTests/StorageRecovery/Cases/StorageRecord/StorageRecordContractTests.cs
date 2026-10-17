using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class StorageRecordContractTests
{
    private const string RecordKey = "contract-record";
    private const string Content = "stored content";

    [Test]
    public async Task AcCq001_TypedStorageHelpersRejectNullArgumentsBeforeWork()
    {
        var key = KeyCodec.Encode(RecordKey);
        var missingTransaction = Assert.ThrowsExactly<ArgumentNullException>(() =>
            StorageRecords.PutRecord<EventIdentity>(null!, key, new(Content, 1, 1)));
        await Assert.That(missingTransaction.ParamName).IsEqualTo("tx");
        var missingView = Assert.ThrowsExactly<ArgumentNullException>(() =>
            StorageRecords.GetRecord<EventIdentity>(null!, key));
        await Assert.That(missingView.ParamName).IsEqualTo("view");

        using var database = new TestDatabase();
        var rejectedKey = database.Store.Commit((transaction, _) =>
        {
            var missingKey = Assert.ThrowsExactly<ArgumentNullException>(() =>
                transaction.PutRecord<EventIdentity>(null!, new(Content, 1, 1)));
            Assert.ThrowsExactly<ArgumentNullException>(() =>
                transaction.GetRecord<EventIdentity>(null!));
            return missingKey.ParamName;
        });
        await Assert.That(rejectedKey).IsEqualTo("key");
        await Assert.That(database.Store.Read(view => view.GetRecord<EventIdentity>(key))).IsNull();
    }

    [Test]
    public async Task AcMp002TypedRecordReadsDecodeBorrowedBytesWithoutAnOwnedPointCopy()
    {
        using var database = new TestDatabase();
        var key = KeyCodec.Encode(RecordKey);
        var expected = new EventIdentity(Content, 1, 1);
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(key, expected);
            return true;
        });
        var before = database.Store.GetReadDiagnostics();
        var actual = database.Store.Read(view => view.GetRecord<EventIdentity>(key));
        var after = database.Store.GetReadDiagnostics();

        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(after.OwnedPointLookups - before.OwnedPointLookups).IsEqualTo(0L);
        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo(1L);
        await Assert.That(after.PointExaminedBytes - before.PointExaminedBytes)
            .IsEqualTo((long)key.Length + NativeSerialization.Measure(expected));
    }

    [Test]
    public async Task AcCq001_TypedRecordsRoundTripThroughTheActualStorageGate()
    {
        using var database = new TestDatabase();
        var key = KeyCodec.Encode(RecordKey);
        var expected = new EventIdentity(Content, 1, 1);
        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(key, expected);
            return true;
        });
        await Assert.That(database.Store.Read(view => view.GetRecord<EventIdentity>(key))).IsEqualTo(expected);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Delete(key);
            return true;
        });
        await Assert.That(database.Store.Read(view => view.GetRecord<EventIdentity>(key))).IsNull();
    }
}
