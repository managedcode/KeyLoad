using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobRawPartFailureTests
{
    private const string MissingBlobId = "missing-raw-part";
    private const string CorruptBlobId = "corrupt-raw-part";
    private const string HealthyBlobId = "healthy-after-corruption";
    private const int PartOrdinal = 0;
    private const int RangeLength = 4;
    private static readonly byte[] OriginalBytes = [0x71, 0x72, 0x73, 0x74];
    private static readonly byte[] CorruptBytes = [0x71, 0x72, 0x73, 0x75];

    [Test]
    public async Task AcBlob002MissingOrCorruptRawPartFailsClosedAndDoesNotPoisonHealthyReads()
    {
        using var db = new TestDatabase();
        BlobStorageTestSupport.Configure(db);
        var operations = new BlobStorageOperations(db.Database);
        var missing = Publish(db, MissingBlobId);
        var corrupt = Publish(db, CorruptBlobId);
        var healthy = Publish(db, HealthyBlobId);
        var missingKey = PartKey(missing.Blob, missing.UploadId, PartOrdinal);
        var corruptKey = PartKey(corrupt.Blob, corrupt.UploadId, PartOrdinal);
        db.Store.Commit((transaction, _) =>
        {
            transaction.Delete(missingKey);
            transaction.Put(corruptKey, CorruptBytes);
            return true;
        });

        var missingFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            operations.Read("root", new(missing.Blob, missing.Revision, 0, RangeLength)));
        var corruptFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            operations.Read("root", new(corrupt.Blob, corrupt.Revision, 0, RangeLength)));
        var healthyRead = operations.Read("root", new(healthy.Blob, healthy.Revision, 0, RangeLength));

        await Assert.That(missingFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(corruptFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(healthyRead.Bytes.Span.SequenceEqual(OriginalBytes)).IsTrue();
    }

    private static PublishedBlob Publish(TestDatabase database, string id)
    {
        var blob = BlobStorageTestSupport.Blob(database, id);
        var uploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(database, blob, uploadId, OriginalBytes.Length, 0);
        BlobStorageTestSupport.Write(database, blob, uploadId, PartOrdinal, OriginalBytes);
        var chain = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, blob, uploadId, OriginalBytes.Length);
        chain = BlobIntegrity.NextHash(chain, PartOrdinal, OriginalBytes.Length, BlobIntegrity.PartHash(OriginalBytes));
        var metadata = BlobStorageTestSupport.Complete(database, blob, uploadId, chain).Value;
        return new(blob, uploadId, metadata.Revision);
    }

    private static byte[] PartKey(BlobRef blob, Guid uploadId, int ordinal) =>
        KeyCodec.Encode("blob-part-v1", blob.Partition.TenantId, blob.Partition.DatabaseId,
            blob.Partition.TransactionDomainId, blob.Partition.PartitionKey, blob.Resource,
            blob.Id, uploadId.ToString("N"), (long)ordinal);

    private sealed record PublishedBlob(BlobRef Blob, Guid UploadId, long Revision);
}
