using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobMissingCurrentVersionTests
{
    private const string PrincipalId = "root";
    private const string BlobId = "missing-current-state";
    private const string HeadSpace = "blob-head-v1";
    private const string StateSpace = "blob-state-v1";
    private const string PartSpace = "blob-part-v1";
    private const string QuotaSpace = "blob-quota-v1";
    private const string GlobalSpace = "blob-global-v1";
    private const string GuidFormat = "N";
    private const int PartOrdinal = 0;
    private const long InitialRevision = 0;
    private const int FutureCommandClockSeconds = 10;
    private static readonly byte[] Payload = [0x41, 0x42, 0x43];
    private static readonly byte[] UnexpectedWrite = [0x51];

    [Test]
    public async Task AcBlob007MissingCurrentStateFailsEveryOperationWithoutEffects()
    {
        using var database = new TestDatabase();
        BlobStorageTestSupport.Configure(database);
        var blob = BlobStorageTestSupport.Blob(database, BlobId);
        var uploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(database, blob, uploadId, Payload.Length, InitialRevision);
        BlobStorageTestSupport.Write(database, blob, uploadId, PartOrdinal, Payload);
        var integrity = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, blob, uploadId, Payload.Length);
        integrity = BlobIntegrity.NextHash(integrity, PartOrdinal, Payload.Length, BlobIntegrity.PartHash(Payload));
        var metadata = BlobStorageTestSupport.Complete(database, blob, uploadId, integrity).Value;
        await Assert.That(metadata.VersionId == uploadId).IsTrue();

        var missingStateKey = StateKey(blob, uploadId);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Delete(missingStateKey);
            return true;
        });
        var before = Capture(database, blob, uploadId);
        var operations = new BlobStorageOperations(database.Database);
        var metadataFailure = Assert.ThrowsExactly<KeyLoadException>(
            () => operations.Metadata(PrincipalId, new(blob)));
        await Assert.That(metadataFailure.Code).IsEqualTo(ErrorCode.Corruption);

        var newUploadId = Guid.NewGuid();
        var beginId = Guid.NewGuid();
        await AssertRejectedCommand(database, blob, uploadId, newUploadId, before,
            OperationKind.BeginBlobUpload, beginId,
            new BeginBlobUploadRequest(beginId, blob, newUploadId, Payload.Length, metadata.Revision));
        var writeId = Guid.NewGuid();
        await AssertRejectedCommand(database, blob, uploadId, newUploadId, before,
            OperationKind.WriteBlobPart, writeId,
            new WriteBlobPartRequest(writeId, blob, uploadId, PartOrdinal, UnexpectedWrite,
                BlobIntegrity.PartHash(UnexpectedWrite)));
        var abortId = Guid.NewGuid();
        await AssertRejectedCommand(database, blob, uploadId, newUploadId, before,
            OperationKind.AbortBlobUpload, abortId,
            new AbortBlobUploadRequest(abortId, blob, uploadId));
        var completeId = Guid.NewGuid();
        var expectedIntegrityHash = metadata.IntegrityHash
            ?? throw new InvalidOperationException("A published version requires an integrity hash.");
        await AssertRejectedCommand(database, blob, uploadId, newUploadId, before,
            OperationKind.CompleteBlobUpload, completeId,
            new CompleteBlobUploadRequest(completeId, blob, uploadId, expectedIntegrityHash));
        await Assert.That(Read(database, StateKey(blob, newUploadId))).IsNull();
    }

    private static async Task AssertRejectedCommand(TestDatabase database, BlobRef blob, Guid missingUploadId,
        Guid newUploadId, BlobStateSnapshot before, OperationKind kind, Guid commandId, object request)
    {
        var exception = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Apply(new(commandId,
            kind, PrincipalId, TimeProvider.System.GetUtcNow().AddSeconds(FutureCommandClockSeconds),
            JsonSerializer.Serialize(request, request.GetType(), JsonDefaults.Options))));

        await Assert.That(exception.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Read(database, OutcomeStoreOracle.PartitionKey(blob.Partition, PrincipalId, commandId))).IsNull();
        await AssertSnapshotUnchanged(database, blob, missingUploadId, newUploadId, before);
    }

    private static async Task AssertSnapshotUnchanged(TestDatabase database, BlobRef blob,
        Guid missingUploadId, Guid newUploadId, BlobStateSnapshot before)
    {
        var after = Capture(database, blob, missingUploadId, newUploadId);
        await Assert.That(after.Position).IsEqualTo(before.Position);
        await Assert.That(after.AppliedPosition).IsEqualTo(before.AppliedPosition);
        await Assert.That(after.Clock is not null && before.Clock is not null
            && after.Clock.AsSpan().SequenceEqual(before.Clock)).IsTrue();
        await Assert.That(after.Head is not null && before.Head is not null
            && after.Head.AsSpan().SequenceEqual(before.Head)).IsTrue();
        await Assert.That(after.State).IsNull();
        await Assert.That(after.NewState).IsNull();
        await Assert.That(after.Quota is not null && before.Quota is not null
            && after.Quota.AsSpan().SequenceEqual(before.Quota)).IsTrue();
        await Assert.That(after.Global is not null && before.Global is not null
            && after.Global.AsSpan().SequenceEqual(before.Global)).IsTrue();
        await Assert.That(after.Part is not null && before.Part is not null
            && after.Part.AsSpan().SequenceEqual(before.Part)).IsTrue();
    }

    private static BlobStateSnapshot Capture(TestDatabase database, BlobRef blob, Guid uploadId, Guid? newUploadId = null) =>
        new(database.Store.Position, database.Database.LastApplied,
            Read(database, KeySpace.Clock.ToArray()), Read(database, HeadKey(blob)), Read(database, StateKey(blob, uploadId)),
            newUploadId is { } newId ? Read(database, StateKey(blob, newId)) : null,
            Read(database, QuotaKey(blob)), Read(database, KeyCodec.Encode(GlobalSpace)),
            Read(database, PartKey(blob, uploadId, PartOrdinal)));

    private static byte[] HeadKey(BlobRef blob) =>
        KeySpace.Partition(HeadSpace, blob.Partition, blob.Resource, blob.Id);

    private static byte[] StateKey(BlobRef blob, Guid uploadId) =>
        KeySpace.Partition(StateSpace, blob.Partition, blob.Resource, blob.Id, uploadId.ToString(GuidFormat));

    private static byte[] QuotaKey(BlobRef blob) => KeyCodec.Encode(QuotaSpace,
        blob.Partition.TenantId, blob.Partition.DatabaseId, blob.Partition.TransactionDomainId, blob.Resource);

    private static byte[] PartKey(BlobRef blob, Guid uploadId, int ordinal) =>
        KeySpace.Partition(PartSpace, blob.Partition, blob.Resource, blob.Id,
            uploadId.ToString(GuidFormat), (long)ordinal);

    private static byte[]? Read(TestDatabase database, byte[] key) =>
        database.Store.Read(view => view.ReadOwnedValue(key));

    private sealed record BlobStateSnapshot(long Position, long AppliedPosition,
        byte[]? Clock, byte[]? Head, byte[]? State, byte[]? NewState,
        byte[]? Quota, byte[]? Global, byte[]? Part);
}
