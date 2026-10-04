using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobOutcomeLifetimeTests
{
    private const string BlobId = "reused-upload-identity";
    private const int PartOrdinal = 0;
    private const int MaxReclaimParts = 1;
    private static readonly byte[] FirstLifetimeBytes = [0x11, 0x22, 0x33];
    private static readonly byte[] SecondLifetimeBytes = [0x44, 0x55, 0x66];

    [Test]
    public async Task AcBlob004ReusedUploadIdCannotReplayAnotherLifetimeOutcome()
    {
        using var db = new TestDatabase();
        BlobStorageTestSupport.Configure(db);
        var operations = new BlobStorageOperations(db.Database);
        var blob = BlobStorageTestSupport.Blob(db, BlobId);
        var uploadId = Guid.NewGuid();
        var firstBeginId = Guid.NewGuid();
        var firstBegin = new BeginBlobUploadRequest(firstBeginId, blob, uploadId, FirstLifetimeBytes.Length, 0);
        Apply(db, OperationKind.BeginBlobUpload, firstBeginId, firstBegin)
            .Get<BlobCommitResult<BlobUploadInfo>>();
        var firstPartId = Guid.NewGuid();
        var firstPart = new WriteBlobPartRequest(firstPartId, blob, uploadId, PartOrdinal,
            FirstLifetimeBytes, BlobIntegrity.PartHash(FirstLifetimeBytes));
        Apply(db, OperationKind.WriteBlobPart, firstPartId, firstPart)
            .Get<BlobCommitResult<BlobUploadInfo>>();
        var abortId = Guid.NewGuid();
        var abort = new AbortBlobUploadRequest(abortId, blob, uploadId);
        Apply(db, OperationKind.AbortBlobUpload, abortId, abort).Get<BlobCommitResult<BlobUploadInfo>>();
        var reclaimId = Guid.NewGuid();
        var reclaim = new ReclaimBlobRequest(reclaimId, blob, uploadId, MaxReclaimParts);
        var reclaimed = Apply(db, OperationKind.ReclaimBlob, reclaimId, reclaim)
            .Get<BlobCommitResult<BlobReclaimResult>>();
        var reclaimReplay = Apply(db, OperationKind.ReclaimBlob, reclaimId, reclaim)
            .Get<BlobCommitResult<BlobReclaimResult>>();

        await Assert.That(reclaimed.Value.Complete).IsTrue();
        await Assert.That(reclaimReplay.Receipt.Token).IsEqualTo(reclaimed.Receipt.Token);
        await Assert.That(operations.UploadInfo("root", new(blob, uploadId))).IsNull();

        var secondBeginId = Guid.NewGuid();
        Apply(db, OperationKind.BeginBlobUpload, secondBeginId,
            new BeginBlobUploadRequest(secondBeginId, blob, uploadId, SecondLifetimeBytes.Length, 0))
            .Get<BlobCommitResult<BlobUploadInfo>>();
        var secondPartId = Guid.NewGuid();
        Apply(db, OperationKind.WriteBlobPart, secondPartId,
            new WriteBlobPartRequest(secondPartId, blob, uploadId, PartOrdinal,
                SecondLifetimeBytes, BlobIntegrity.PartHash(SecondLifetimeBytes)))
            .Get<BlobCommitResult<BlobUploadInfo>>();

        await AssertOldLifetimeInvalidated(db, firstBeginId, firstBegin, OperationKind.BeginBlobUpload);
        await AssertOldLifetimeInvalidated(db, firstPartId, firstPart,
            OperationKind.WriteBlobPart);
        await AssertOldLifetimeInvalidated(db, abortId, abort, OperationKind.AbortBlobUpload);
        await AssertOldLifetimeInvalidated(db, reclaimId, reclaim, OperationKind.ReclaimBlob);

        var active = operations.UploadInfo("root", new(blob, uploadId));
        await Assert.That(active!.Status).IsEqualTo(BlobUploadStatus.Active);
        await Assert.That(active.StoredBytes).IsEqualTo(SecondLifetimeBytes.Length);
    }

    private static async Task AssertOldLifetimeInvalidated<T>(TestDatabase database, Guid commandId,
        T payload, OperationKind kind)
    {
        var result = Apply(database, kind, commandId, payload);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    private static OperationResult Apply<T>(TestDatabase database, OperationKind kind, Guid commandId, T payload)
        => database.Submit(kind, payload, id: commandId);
}
