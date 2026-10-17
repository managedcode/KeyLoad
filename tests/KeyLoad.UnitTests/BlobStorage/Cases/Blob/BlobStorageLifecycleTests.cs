using System.Text;
using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobStorageLifecycleTests
{
    private const string PrincipalId = "root";
    private const string EmptyBlobId = "empty-object";
    private const string SingleBlobId = "single-object";
    private const string MultipartBlobId = "multipart-object";
    private const int RawPartBytes = 65_536;
    private const int TailBytes = 9;
    private const int InteriorOffset = 123;
    private const int InteriorCount = 5;
    private const int BoundaryOffset = RawPartBytes - 2;
    private const int CrossBoundaryCount = 4;
    private const int FinalRangeCount = 3;

    [Test]
    public async Task AcBlob001EmptyAndSinglePartStayHiddenUntilCompleteAndCommandRetriesAreStable()
    {
        using var db = new TestDatabase();
        BlobStorageTestSupport.Configure(db);
        var operations = new BlobStorageOperations(db.Database);
        var empty = BlobStorageTestSupport.Blob(db, EmptyBlobId);
        var emptyUploadId = Guid.NewGuid();
        var beginId = Guid.NewGuid();
        var begun = BlobStorageTestSupport.Begin(db, empty, emptyUploadId, 0, 0, beginId);
        var repeatedBegin = BlobStorageTestSupport.Begin(db, empty, emptyUploadId, 0, 0, beginId);

        await Assert.That(begun.Receipt.Token).IsEqualTo(repeatedBegin.Receipt.Token);
        await Assert.That(operations.Metadata(PrincipalId, new(empty))).IsNull();
        await Assert.That(operations.UploadInfo(PrincipalId, new(empty, emptyUploadId))!.Status)
            .IsEqualTo(BlobUploadStatus.Active);

        var initialHash = BlobIntegrity.InitialHash(db.Store.Identity.Incarnation, empty, emptyUploadId, 0);
        var completeId = Guid.NewGuid();
        var completed = BlobStorageTestSupport.Complete(db, empty, emptyUploadId, initialHash, completeId);
        var completedAgain = BlobStorageTestSupport.Complete(db, empty, emptyUploadId, initialHash, completeId);
        var emptyMetadata = operations.Metadata(PrincipalId, new(empty));

        await Assert.That(completed.Receipt.Token).IsEqualTo(completedAgain.Receipt.Token);
        await Assert.That(completed.Receipt.Mutations).IsEmpty();
        await Assert.That(emptyMetadata!.Revision).IsEqualTo(1L);
        await Assert.That(emptyMetadata.Length).IsEqualTo(0L);
        await Assert.That(emptyMetadata.PartCount).IsEqualTo(0);
        await Assert.That(emptyMetadata.IntegrityHash).IsEqualTo(initialHash);
        await Assert.That(operations.Read(PrincipalId, new(empty, 1, 0, 0)).Bytes.IsEmpty).IsTrue();

        var single = BlobStorageTestSupport.Blob(db, SingleBlobId);
        var bytes = Encoding.UTF8.GetBytes("binary\0payload");
        var singleUploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(db, single, singleUploadId, bytes.Length, 0);
        BlobStorageTestSupport.Write(db, single, singleUploadId, 0, bytes);
        var chain = BlobIntegrity.NextHash(
            BlobIntegrity.InitialHash(db.Store.Identity.Incarnation, single, singleUploadId, bytes.Length),
            0, bytes.Length, BlobIntegrity.PartHash(bytes));
        var singleMetadata = BlobStorageTestSupport.Complete(db, single, singleUploadId, chain).Value;
        var read = operations.Read(PrincipalId, new(single, singleMetadata.Revision, 0, bytes.Length));

        await Assert.That(singleMetadata.Length).IsEqualTo(bytes.Length);
        await Assert.That(singleMetadata.PartCount).IsEqualTo(1);
        await Assert.That(read.Bytes.Span.SequenceEqual(bytes)).IsTrue();
        await Assert.That(read.Metadata).IsEqualTo(singleMetadata);
    }

    [Test]
    public async Task AcBlob001OrderedMultipartRetryAndAcBlob002BoundaryRangesPreserveExactBytes()
    {
        using var db = new TestDatabase();
        BlobStorageTestSupport.Configure(db);
        var operations = new BlobStorageOperations(db.Database);
        var blob = BlobStorageTestSupport.Blob(db, MultipartBlobId);
        var (bytes, metadata) = await PublishMultipart(db, operations, blob);
        await AssertMultipartRanges(operations, blob, metadata, bytes);
    }

    private static async Task<(byte[] Bytes, BlobMetadata Metadata)> PublishMultipart(
        TestDatabase db, BlobStorageOperations operations, BlobRef blob)
    {
        var bytes = Enumerable.Range(0, RawPartBytes + TailBytes).Select(static value => (byte)(value % 251)).ToArray();
        var firstPart = bytes.AsMemory(0, RawPartBytes);
        var tailPart = bytes.AsMemory(RawPartBytes);
        var uploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(db, blob, uploadId, bytes.Length, 0);

        var outOfOrder = BlobStorageTestSupport.WriteResult(db, blob, uploadId, 1, tailPart);
        await Assert.That(outOfOrder.Error is ErrorCode.Validation or ErrorCode.Conflict).IsTrue();
        await Assert.That(operations.Metadata(PrincipalId, new(blob))).IsNull();
        var firstWrite = BlobStorageTestSupport.Write(db, blob, uploadId, 0, firstPart);
        var duplicatePart = BlobStorageTestSupport.Write(db, blob, uploadId, 0, firstPart);
        var afterDuplicate = operations.UploadInfo(PrincipalId, new(blob, uploadId))!;
        await Assert.That(firstWrite.Receipt.Token).IsNotEqualTo(duplicatePart.Receipt.Token);
        await Assert.That(afterDuplicate.StoredBytes).IsEqualTo(RawPartBytes);
        await Assert.That(afterDuplicate.NextOrdinal).IsEqualTo(1);

        BlobStorageTestSupport.Write(db, blob, uploadId, 1, tailPart);
        var chain = BlobIntegrity.InitialHash(db.Store.Identity.Incarnation, blob, uploadId, bytes.Length);
        chain = BlobIntegrity.NextHash(chain, 0, firstPart.Length, BlobIntegrity.PartHash(firstPart.Span));
        chain = BlobIntegrity.NextHash(chain, 1, tailPart.Length, BlobIntegrity.PartHash(tailPart.Span));
        return (bytes, BlobStorageTestSupport.Complete(db, blob, uploadId, chain).Value);
    }

    private static async Task AssertMultipartRanges(BlobStorageOperations operations,
        BlobRef blob, BlobMetadata metadata, byte[] bytes)
    {
        var beginning = operations.Read(PrincipalId, new(blob, metadata.Revision, 0, CrossBoundaryCount));
        var interior = operations.Read(PrincipalId, new(blob, metadata.Revision, InteriorOffset, InteriorCount));
        var crossing = operations.Read(PrincipalId, new(blob, metadata.Revision, BoundaryOffset, CrossBoundaryCount));
        var final = operations.Read(PrincipalId, new(blob, metadata.Revision, bytes.Length - FinalRangeCount, FinalRangeCount));
        var emptyAtEnd = operations.Read(PrincipalId, new(blob, metadata.Revision, bytes.Length, 0));

        await Assert.That(metadata.PartCount).IsEqualTo(2);
        await Assert.That(metadata.Length).IsEqualTo(bytes.Length);
        await Assert.That(beginning.Bytes.Span.SequenceEqual(bytes.AsSpan(0, CrossBoundaryCount))).IsTrue();
        await Assert.That(interior.Bytes.Span.SequenceEqual(bytes.AsSpan(InteriorOffset, InteriorCount))).IsTrue();
        await Assert.That(crossing.Bytes.Span.SequenceEqual(bytes.AsSpan(BoundaryOffset, CrossBoundaryCount))).IsTrue();
        await Assert.That(final.Bytes.Span.SequenceEqual(bytes.AsSpan(bytes.Length - FinalRangeCount, FinalRangeCount))).IsTrue();
        await Assert.That(emptyAtEnd.Offset).IsEqualTo(bytes.Length);
        await Assert.That(emptyAtEnd.Bytes.IsEmpty).IsTrue();

        var stale = Assert.ThrowsExactly<KeyLoadException>(() =>
            operations.Read(PrincipalId, new(blob, metadata.Revision + 1, 0, 1)));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.RevisionConflict);
        var invalidRange = Assert.ThrowsExactly<KeyLoadException>(() =>
            operations.Read(PrincipalId, new(blob, metadata.Revision, -1, 1)));
        await Assert.That(invalidRange.Code).IsEqualTo(ErrorCode.Validation);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => operations.Read(PrincipalId,
            new(blob, metadata.Revision, 0, 1), cancellation.Token));
        await Assert.That(operations.Read(PrincipalId, new(blob, metadata.Revision, 0, 1))
            .Bytes.Span.SequenceEqual(bytes.AsSpan(0, 1))).IsTrue();
    }
}
