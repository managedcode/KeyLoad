using KeyLoad.Core.Features.BlobStorage;
using ManagedCode.Communication;

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
        var beginId = Guid.NewGuid();
        var beginCut = (Token: db.NextCommitToken(blob.Partition), Position: db.Store.Position);
        var begun = BlobStorageTestSupport.Begin(db, blob, uploadId, ValidBytes.Length, RevisionZero, beginId);
        await BlobWholeFlowAssertions.Receipt(db.Database, begun.Receipt, beginId, blob, beginCut.Token, beginCut.Position);
        var beforeInvalid = BlobRejectedCommandColdFlow.DomainImage(db.Database, blob, uploadId);
        var invalidId = Guid.NewGuid();

        var invalidHash = SubmitPart(db, blob, uploadId, ValidBytes, IncorrectHash, invalidId);
        await Assert.That(Errors.Code(invalidHash.Problem)).IsEqualTo(ErrorCode.Validation);
        await BlobRejectedCommandColdFlow.DomainUnchanged(db.Database, blob, uploadId, beforeInvalid);
        var afterInvalidHash = operations.UploadInfo(PrincipalId, new(blob, uploadId))!;
        await Assert.That(afterInvalidHash.StoredBytes).IsEqualTo(0L);
        await Assert.That(afterInvalidHash.NextOrdinal).IsEqualTo(PartOrdinal);

        var writeId = Guid.NewGuid();
        var writeCut = (Token: db.NextCommitToken(blob.Partition), Position: db.Store.Position);
        var written = BlobStorageTestSupport.Write(db, blob, uploadId, PartOrdinal, ValidBytes, writeId);
        await BlobWholeFlowAssertions.Receipt(db.Database, written.Receipt, writeId, blob, writeCut.Token, writeCut.Position);
        var beforeDuplicate = BlobRejectedCommandColdFlow.DomainImage(db.Database, blob, uploadId);
        var changedId = Guid.NewGuid();
        var changedHash = BlobIntegrity.PartHash(ChangedBytes);
        var changedDuplicate = SubmitPart(db, blob, uploadId, ChangedBytes, changedHash, changedId);
        await Assert.That(Errors.Code(changedDuplicate.Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(operations.UploadInfo(PrincipalId, new(blob, uploadId))!.StoredBytes)
            .IsEqualTo((long)ValidBytes.Length);

        await BlobRejectedCommandColdFlow.DomainUnchanged(db.Database, blob, uploadId, beforeDuplicate);
        var wrongId = Guid.NewGuid();
        var wrongRequest = new CompleteBlobUploadRequest(wrongId, blob, uploadId, IncorrectHash);
        var wrongChain = db.Submit(OperationKind.CompleteBlobUpload, wrongRequest, id: wrongId);
        await Assert.That(Errors.Code(wrongChain.Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(operations.Metadata(PrincipalId, new(blob))).IsNull();
        await BlobRejectedCommandColdFlow.DomainUnchanged(db.Database, blob, uploadId, beforeDuplicate);

        var chain = BlobIntegrity.InitialHash(db.Store.Identity.Incarnation, blob, uploadId, ValidBytes.Length);
        chain = BlobIntegrity.NextHash(chain, PartOrdinal, ValidBytes.Length, BlobIntegrity.PartHash(ValidBytes));
        var completeId = Guid.NewGuid();
        var completeCut = (Token: db.NextCommitToken(blob.Partition), Position: db.Store.Position);
        var committed = BlobStorageTestSupport.Complete(db, blob, uploadId, chain, completeId);
        var published = committed.Value;
        var read = operations.Read(PrincipalId, new(blob, published.Revision, 0, ValidBytes.Length));
        await Assert.That(read.Bytes.Span.SequenceEqual(ValidBytes)).IsTrue();
        await Assert.That(published.Revision).IsEqualTo(1L);
        await BlobWholeFlowAssertions.Receipt(db.Database, committed.Receipt, completeId, blob,
            completeCut.Token, completeCut.Position);
        await BlobRejectedCommandColdFlow.Run(db, blob, uploadId, begun, written, published, ValidBytes,
        [
            BlobRejectedCommandColdFlow.Retain(invalidId, OperationKind.WriteBlobPart,
                new WriteBlobPartRequest(invalidId, blob, uploadId, PartOrdinal, ValidBytes, IncorrectHash), invalidHash),
            BlobRejectedCommandColdFlow.Retain(changedId, OperationKind.WriteBlobPart,
                new WriteBlobPartRequest(changedId, blob, uploadId, PartOrdinal, ChangedBytes, changedHash), changedDuplicate),
            BlobRejectedCommandColdFlow.Retain(wrongId, OperationKind.CompleteBlobUpload, wrongRequest, wrongChain)
        ]);
    }

    private static Result<object> SubmitPart(TestDatabase db, BlobRef blob, Guid uploadId,
        ReadOnlyMemory<byte> bytes, string sha256, Guid id)
    {
        var request = new WriteBlobPartRequest(id, blob, uploadId, PartOrdinal, bytes, sha256);
        return db.Submit(OperationKind.WriteBlobPart, request, id: id);
    }
}
