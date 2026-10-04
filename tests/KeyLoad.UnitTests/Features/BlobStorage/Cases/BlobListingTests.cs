using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobListingTests
{
    private const string ReaderId = "blob-list-reader";
    private const string ReaderOwnerId = "reader-owner";
    private const string OtherOwnerId = "other-owner";
    private const string ResourceName = "blobs";
    private const string FirstHiddenId = "a-hidden";
    private const string FirstVisibleId = "b-visible";
    private const string SecondHiddenId = "c-hidden";
    private const string SecondVisibleId = "d-visible";
    private const int PageSize = 1;
    private static readonly byte[] Bytes = [0x61, 0x62];

    [Test]
    public async Task AcBlob006ListingSkipsUnreadableRowsAndAdvancesExclusiveBoundedCursor()
    {
        using var db = new TestDatabase();
        BlobStorageTestSupport.Configure(db);
        ConfigureReader(db);
        Publish(db, FirstHiddenId, OtherOwnerId);
        Publish(db, FirstVisibleId, ReaderOwnerId);
        Publish(db, SecondHiddenId, OtherOwnerId);
        Publish(db, SecondVisibleId, ReaderOwnerId);
        var operations = new BlobStorageOperations(db.Database);

        var first = operations.List(ReaderId, new(db.Partition, ResourceName, PageSize));
        var second = operations.List(ReaderId, new(db.Partition, ResourceName, PageSize, first.NextAfterId));
        var exhausted = operations.List(ReaderId, new(db.Partition, ResourceName, PageSize, second.NextAfterId));

        await Assert.That(first.Items.Length).IsEqualTo(PageSize);
        await Assert.That(first.Items[0].Blob.Id).IsEqualTo(FirstVisibleId);
        await Assert.That(first.NextAfterId).IsEqualTo(FirstVisibleId);
        await Assert.That(second.Items.Length).IsEqualTo(PageSize);
        await Assert.That(second.Items[0].Blob.Id).IsEqualTo(SecondVisibleId);
        await Assert.That(second.NextAfterId).IsEqualTo(SecondVisibleId);
        await Assert.That(exhausted.Items.IsEmpty).IsTrue();
        await Assert.That(exhausted.NextAfterId).IsNull();
    }

    private static void Publish(TestDatabase database, string id, string ownerId)
    {
        var blob = BlobStorageTestSupport.Blob(database, id);
        var uploadId = Guid.NewGuid();
        var beginId = Guid.NewGuid();
        database.Submit(OperationKind.BeginBlobUpload,
            new BeginBlobUploadRequest(beginId, blob, uploadId, Bytes.Length, 0, new(ownerId)), id: beginId)
            .Get<BlobCommitResult<BlobUploadInfo>>();
        BlobStorageTestSupport.Write(database, blob, uploadId, 0, Bytes);
        var chain = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, blob, uploadId, Bytes.Length);
        chain = BlobIntegrity.NextHash(chain, 0, Bytes.Length, BlobIntegrity.PartHash(Bytes));
        BlobStorageTestSupport.Complete(database, blob, uploadId, chain);
    }

    private static void ConfigureReader(TestDatabase database)
    {
        var principal = new PrincipalRecord(ReaderId, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, ResourceName, Capability.BlobRead)], [])
        {
            OwnerId = ReaderOwnerId,
            RestrictRows = true
        };
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal))
            .Get<PrincipalRecord>();
    }
}
