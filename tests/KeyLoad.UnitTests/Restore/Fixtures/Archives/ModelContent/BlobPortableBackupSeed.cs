using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BlobPortableBackupSeed
{
    private const string MissingCommittedClock = "The original blob completion has no committed clock.";
    internal const string Root = "root";
    internal const string Resource = "backup_blobs";
    internal const string Published = "published";
    internal const string Active = "active";
    internal const string Fresh = "fresh";
    internal static byte[] Payload() => Enumerable.Range(0, BlobLimits.RawPartBytes + 1)
        .Select(static index => (byte)(index % 251)).ToArray();

    internal static (ReplicatedOperation Operation, OperationResult Result) Send<T>(DatabaseEngine database,
        OperationKind kind, Guid id, T request, CancellationToken token)
    {
        var operation = database.CreateNativeOperation(kind, id, Root,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var result = database.ApplyEmbedded(operation, token);
        if (result.Error is not null)
        {
            throw new InvalidOperationException("The genuine portable blob seed operation was rejected.");
        }
        return (operation, result);
    }

    internal static async Task<(BlobMetadata Metadata, (ReplicatedOperation Operation, OperationResult Result)[] Originals)>
        PublishAsync(DatabaseEngine database, BlobRef blob, byte[] bytes, CancellationToken token)
    {
        var upload = Guid.NewGuid();
        var id = Guid.NewGuid();
        var begin = Send(database, OperationKind.BeginBlobUpload, id,
            new BeginBlobUploadRequest(id, blob, upload, bytes.Length, 0), token);
        await BlobPortableBackupAssertions.ReceiptAsync(database, blob,
            begin.Result.Get<BlobCommitResult<BlobUploadInfo>>().Receipt, id);
        var first = await PartAsync(database, blob, upload, 0, bytes.AsMemory(0, BlobLimits.RawPartBytes), token);
        var second = await PartAsync(database, blob, upload, 1, bytes.AsMemory(BlobLimits.RawPartBytes), token);
        var hash = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, blob, upload, bytes.Length);
        hash = BlobIntegrity.NextHash(hash, 0, BlobLimits.RawPartBytes, BlobIntegrity.PartHash(bytes.AsSpan(0, BlobLimits.RawPartBytes)));
        hash = BlobIntegrity.NextHash(hash, 1, 1, BlobIntegrity.PartHash(bytes.AsSpan(BlobLimits.RawPartBytes)));
        id = Guid.NewGuid();
        var complete = Send(database, OperationKind.CompleteBlobUpload, id,
            new CompleteBlobUploadRequest(id, blob, upload, hash), token);
        var result = complete.Result.Get<BlobCommitResult<BlobMetadata>>();
        await BlobPortableBackupAssertions.ReceiptAsync(database, blob, result.Receipt, id);
        var committedAt = database.Store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(
            view.ReadOwnedValue(KeySpace.ClockBytes) ?? throw new InvalidOperationException(MissingCommittedClock)));
        await Assert.That(committedAt).IsGreaterThanOrEqualTo(complete.Operation.EvaluatedAt);
        await BackupPersistedPolicyAssertions.SameAsync(new BlobMetadata(blob, 1L, upload, bytes.Length,
            2, hash, new RowAccess(), committedAt), result.Value);
        return (result.Value, [begin, first, second, complete]);
    }

    internal static async Task<(ReplicatedOperation Operation, OperationResult Result)> PartAsync(
        DatabaseEngine database, BlobRef blob, Guid upload, int ordinal, ReadOnlyMemory<byte> bytes,
        CancellationToken token)
    {
        var id = Guid.NewGuid();
        var result = Send(database, OperationKind.WriteBlobPart, id,
            new WriteBlobPartRequest(id, blob, upload, ordinal, bytes, BlobIntegrity.PartHash(bytes.Span)), token);
        await BlobPortableBackupAssertions.ReceiptAsync(database, blob,
            result.Result.Get<BlobCommitResult<BlobUploadInfo>>().Receipt, id);
        return result;
    }
}
