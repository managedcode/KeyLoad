using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobReclaimTests
{
    private const string BlobId = "retained-version";
    private const string ExpiredBlobId = "expired-upload";
    private const int PartLimit = 1;
    private const int PartTailLength = 1;
    private const long InitialRevision = 0;
    private const long PublishedRevision = 1;
    private static readonly byte[] Payload = Enumerable.Range(0, BlobLimits.RawPartBytes + PartTailLength)
        .Select(static value => (byte)(value % 239)).ToArray();

    [Test]
    public async Task AcBlob004DeleteRetiresCurrentVersionBeforeBoundedReclaimAndFinalReceiptReplay()
    {
        using var db = new TestDatabase();
        BlobStorageTestSupport.Configure(db);
        var blob = BlobStorageTestSupport.Blob(db, BlobId);
        var metadata = PublishMultipart(db, blob);
        var uploadId = metadata.VersionId ?? throw new InvalidOperationException("Published test data must have a version.");
        var attempted = Reclaim(db, blob, uploadId, PartLimit, Guid.NewGuid());
        await Assert.That(attempted.Error).IsEqualTo(ErrorCode.Conflict);

        var deleted = Delete(db, blob, metadata.Revision);
        var tombstone = deleted.Value;
        var operations = new BlobStorageOperations(db.Database);
        await Assert.That(tombstone.Deleted).IsTrue();
        await Assert.That(tombstone.Revision).IsEqualTo(PublishedRevision + 1);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(
            () => operations.Read("root", new(blob, tombstone.Revision, 0, 1))).Code)
            .IsEqualTo(ErrorCode.NotFound);

        var first = ReclaimTyped(db, blob, uploadId, PartLimit);
        var finalCommandId = Guid.NewGuid();
        var final = ReclaimTyped(db, blob, uploadId, PartLimit, finalCommandId);
        var replay = ReclaimTyped(db, blob, uploadId, PartLimit, finalCommandId);
        await Assert.That(first.Value.DeletedParts).IsEqualTo(PartLimit);
        await Assert.That(first.Value.RemainingParts).IsEqualTo(PartLimit);
        await Assert.That(first.Value.ReleasedBytes).IsEqualTo(BlobLimits.RawPartBytes);
        await Assert.That(first.Value.Complete).IsFalse();
        await Assert.That(final.Value.DeletedParts).IsEqualTo(PartLimit);
        await Assert.That(final.Value.RemainingParts).IsEqualTo(0);
        await Assert.That(final.Value.ReleasedBytes).IsEqualTo(PartTailLength);
        await Assert.That(final.Value.Complete).IsTrue();
        await Assert.That(replay.Receipt.Token).IsEqualTo(final.Receipt.Token);
        await Assert.That(operations.UploadInfo("root", new(blob, uploadId))).IsNull();
        await Assert.That(db.Store.Read(view => view.ReadOwnedValue(PartKey(blob, uploadId, 0)))).IsNull();
        await Assert.That(db.Store.Read(view => view.ReadOwnedValue(PartKey(blob, uploadId, PartLimit)))).IsNull();
    }

    [Test]
    public async Task AcBlob005ExpiredUploadReclaimReleasesUnusedReservationExactlyOnce()
    {
        using var db = new TestDatabase();
        ConfigureShortTtl(db);
        var blob = BlobStorageTestSupport.Blob(db, ExpiredBlobId);
        var uploadId = Guid.NewGuid();
        var beginId = Guid.NewGuid();
        var startedAt = TimeProvider.System.GetUtcNow();
        db.Submit(OperationKind.BeginBlobUpload,
            new BeginBlobUploadRequest(beginId, blob, uploadId, PartTailLength + 1L, InitialRevision),
            id: beginId, time: startedAt).Get<BlobCommitResult<BlobUploadInfo>>();

        var beforeExpiry = Reclaim(db, blob, uploadId, PartLimit, Guid.NewGuid(),
            startedAt.AddSeconds(BlobLimits.MinimumUploadTtlSeconds - 1));
        await Assert.That(beforeExpiry.Error).IsEqualTo(ErrorCode.Conflict);

        var reclaimId = Guid.NewGuid();
        var expired = ReclaimTyped(db, blob, uploadId, PartLimit, reclaimId,
            startedAt.AddSeconds(BlobLimits.MinimumUploadTtlSeconds));
        var replay = ReclaimTyped(db, blob, uploadId, PartLimit, reclaimId,
            startedAt.AddSeconds(BlobLimits.MinimumUploadTtlSeconds));
        var empty = ReclaimTyped(db, blob, uploadId, PartLimit, Guid.NewGuid(),
            startedAt.AddSeconds(BlobLimits.MinimumUploadTtlSeconds));

        await Assert.That(expired.Value.ReleasedBytes).IsEqualTo(PartTailLength + 1L);
        await Assert.That(expired.Value.DeletedParts).IsEqualTo(0);
        await Assert.That(expired.Value.Complete).IsTrue();
        await Assert.That(replay.Receipt.Token).IsEqualTo(expired.Receipt.Token);
        await Assert.That(empty.Value.ReleasedBytes).IsEqualTo(0L);
        await Assert.That(empty.Value.Complete).IsTrue();
    }

    private static BlobMetadata PublishMultipart(TestDatabase database, BlobRef blob)
    {
        var uploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(database, blob, uploadId, Payload.Length, InitialRevision);
        BlobStorageTestSupport.Write(database, blob, uploadId, 0, Payload.AsMemory(0, BlobLimits.RawPartBytes));
        BlobStorageTestSupport.Write(database, blob, uploadId, PartLimit, Payload.AsMemory(BlobLimits.RawPartBytes));
        var chain = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, blob, uploadId, Payload.Length);
        chain = BlobIntegrity.NextHash(chain, 0, BlobLimits.RawPartBytes,
            BlobIntegrity.PartHash(Payload.AsSpan(0, BlobLimits.RawPartBytes)));
        chain = BlobIntegrity.NextHash(chain, PartLimit, PartTailLength,
            BlobIntegrity.PartHash(Payload.AsSpan(BlobLimits.RawPartBytes)));
        return BlobStorageTestSupport.Complete(database, blob, uploadId, chain).Value;
    }

    private static BlobCommitResult<BlobMetadata> Delete(TestDatabase database, BlobRef blob, long revision)
    {
        var id = Guid.NewGuid();
        return database.Submit(OperationKind.DeleteBlob, new DeleteBlobRequest(id, blob, revision), id: id)
            .Get<BlobCommitResult<BlobMetadata>>();
    }

    private static byte[] PartKey(BlobRef blob, Guid uploadId, int ordinal) =>
        KeyCodec.Encode("blob-part-v1", blob.Partition.TenantId, blob.Partition.DatabaseId,
            blob.Partition.TransactionDomainId, blob.Partition.PartitionKey, blob.Resource,
            blob.Id, uploadId.ToString("N"), (long)ordinal);

    private static OperationResult Reclaim(TestDatabase database, BlobRef blob,
        Guid uploadId, int maxParts, Guid commandId, DateTimeOffset? time = null)
    {
        var request = new ReclaimBlobRequest(commandId, blob, uploadId, maxParts);
        return database.Submit(OperationKind.ReclaimBlob, request, id: commandId, time: time);
    }

    private static BlobCommitResult<BlobReclaimResult> ReclaimTyped(TestDatabase database, BlobRef blob,
        Guid uploadId, int maxParts, Guid? commandId = null, DateTimeOffset? time = null)
    {
        var id = commandId ?? Guid.NewGuid();
        return Reclaim(database, blob, uploadId, maxParts, id, time).Get<BlobCommitResult<BlobReclaimResult>>();
    }

    private static void ConfigureShortTtl(TestDatabase database)
    {
        var definition = new ResourceDefinition("blobs", ResourceKind.BlobStore,
            database.Partition.TransactionDomainId)
        { BlobPolicy = new() { UploadTtlSeconds = BlobLimits.MinimumUploadTtlSeconds } };
        database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition))
            .Get<ResourceDefinition>();
    }
}
