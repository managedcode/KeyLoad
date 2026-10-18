using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BlobPortableBackupAssertions
{
    internal static async Task ReceiptAsync(DatabaseEngine database, BlobRef blob, CommitReceipt receipt, Guid id)
    {
        await BackupPersistedPolicyAssertions.ReceiptAsync(database, blob.Partition, id, receipt);
        await Assert.That(receipt.Mutations).IsEmpty();
    }

    internal static async Task RangesAsync(DatabaseEngine database, BlobMetadata metadata,
        byte[] payload, CancellationToken token)
    {
        var before = BackupPersistedPolicyAssertions.Capture(database.Store);
        var blobs = new BlobStorageOperations(database);
        await BackupPersistedPolicyAssertions.SameAsync(metadata,
            blobs.Metadata(BlobPortableBackupSeed.Root, new(metadata.Blob), token));
        foreach (var range in new[] { (0L, 4), ((long)BlobLimits.RawPartBytes - 2, 3),
            ((long)BlobLimits.RawPartBytes, 1), ((long)payload.Length, 0) })
        {
            var read = blobs.Read(BlobPortableBackupSeed.Root,
                new(metadata.Blob, metadata.Revision, range.Item1, range.Item2), token);
            await BackupPersistedPolicyAssertions.SameAsync(metadata, read.Metadata);
            await Assert.That(read.Bytes.Span.SequenceEqual(payload.AsSpan((int)range.Item1, range.Item2))).IsTrue();
        }
        await BackupPersistedPolicyAssertions.UnchangedAsync(database.Store, before);
    }

    internal static async Task ReplayAsync(DatabaseEngine database,
        (Command<DatabaseCommand> Operation, Result<object> Result)[] originals, CancellationToken token)
    {
        foreach (var original in originals)
        {
            await BackupPersistedPolicyAssertions.ReplayAsync(database, original.Operation, original.Result, token);
        }
    }

    internal static async Task ActiveAsync(DatabaseEngine database, BlobUploadInfo original,
        byte[] accepted, CancellationToken token)
    {
        var before = BackupPersistedPolicyAssertions.Capture(database.Store);
        var blobs = new BlobStorageOperations(database);
        await BackupPersistedPolicyAssertions.SameAsync(original with
        {
            Status = BlobUploadStatus.Aborted
        },
            blobs.UploadInfo(BlobPortableBackupSeed.Root, new(original.Blob, original.UploadId), token));
        var bytes = database.Store.Read(view => view.ReadOwnedValue(BlobKeys.Part(original.Blob, original.UploadId, 0)));
        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes!.AsSpan().SequenceEqual(accepted)).IsTrue();
        await Assert.That(blobs.Metadata(BlobPortableBackupSeed.Root, new(original.Blob), token)).IsNull();
        await BackupPersistedPolicyAssertions.UnchangedAsync(database.Store, before);
    }

    internal static async Task FencedAsync(DatabaseEngine database, BlobMetadata metadata,
        Command<DatabaseCommand> source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var before = BackupPersistedPolicyAssertions.Capture(database.Store);
        var signed = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(source));
        await Assert.That(signed.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(signed.Message).IsEqualTo(BlobErrors.Fenced);
        var read = Assert.ThrowsExactly<KeyLoadException>(() => new BlobStorageOperations(database)
            .Metadata(BlobPortableBackupSeed.Root, new(metadata.Blob), token));
        await Assert.That(read.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(read.Message).IsEqualTo(BlobErrors.Fenced);
        await BackupPersistedPolicyAssertions.UnchangedAsync(database.Store, before);
    }
}
