using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobStorageRejectedCommandTests
{
    private const string PrincipalId = "root";
    private const string BlobId = "rejected-command-object";
    private const int PartOrdinal = 0;
    private const int RevisionZero = 0;
    private const string IncorrectHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly byte[] ValidBytes = [0x10, 0x20, 0x30, 0x40];
    private static readonly byte[] ChangedBytes = [0x10, 0x20, 0x30, 0x41];

    [Test]
    public async Task AcBlob001InvalidHashChangedDuplicateAndWrongChainNeverPublishPartialData()
    {
        using var db = new TestDatabase();
        BlobStorageTestSupport.Configure(db);
        var operations = new BlobStorageOperations(db.Database);
        var blob = BlobStorageTestSupport.Blob(db, BlobId);
        var uploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(db, blob, uploadId, ValidBytes.Length, RevisionZero);

        var invalidHash = SubmitPart(db, blob, uploadId, ValidBytes, IncorrectHash);
        await Assert.That(invalidHash.Error).IsEqualTo(ErrorCode.Validation);
        var afterInvalidHash = operations.UploadInfo(PrincipalId, new(blob, uploadId))!;
        await Assert.That(afterInvalidHash.StoredBytes).IsEqualTo(0L);
        await Assert.That(afterInvalidHash.NextOrdinal).IsEqualTo(PartOrdinal);

        BlobStorageTestSupport.Write(db, blob, uploadId, PartOrdinal, ValidBytes);
        var changedDuplicate = BlobStorageTestSupport.WriteResult(db, blob, uploadId, PartOrdinal, ChangedBytes);
        await Assert.That(changedDuplicate.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(operations.UploadInfo(PrincipalId, new(blob, uploadId))!.StoredBytes)
            .IsEqualTo(ValidBytes.Length);

        var wrongChain = BlobStorageTestSupport.CompleteResult(db, blob, uploadId, IncorrectHash);
        await Assert.That(wrongChain.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(operations.Metadata(PrincipalId, new(blob))).IsNull();

        var chain = BlobIntegrity.InitialHash(db.Store.Identity.Incarnation, blob, uploadId, ValidBytes.Length);
        chain = BlobIntegrity.NextHash(chain, PartOrdinal, ValidBytes.Length, BlobIntegrity.PartHash(ValidBytes));
        var published = BlobStorageTestSupport.Complete(db, blob, uploadId, chain).Value;
        var read = operations.Read(PrincipalId, new(blob, published.Revision, 0, ValidBytes.Length));
        await Assert.That(read.Bytes.Span.SequenceEqual(ValidBytes)).IsTrue();
        await Assert.That(published.Revision).IsEqualTo(1L);
    }

    private static OperationResult SubmitPart(TestDatabase db, BlobRef blob, Guid uploadId,
        ReadOnlyMemory<byte> bytes, string sha256)
    {
        var id = Guid.NewGuid();
        var request = new WriteBlobPartRequest(id, blob, uploadId, PartOrdinal, bytes, sha256);
        return db.Submit(OperationKind.WriteBlobPart, request, id: id);
    }
}
