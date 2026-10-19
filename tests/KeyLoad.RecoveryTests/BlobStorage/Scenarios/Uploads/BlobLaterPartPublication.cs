using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobLaterPartPublication
{
    internal static async Task<BlobPriorPublicationState> PublishAsync(DatabaseEngine owner, BlobRef blob,
        Guid upload, byte[] identity, byte[] principal)
    {
        var bytes = BlobLaterPartOracle.Payload();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, 65_553, 0);
        var begun = BlobPriorPublicationCommands.Send(owner, OperationKind.BeginBlobUpload, begin.CommandId, begin);
        var initial = BlobLaterPartOracle.Initial(owner.Store.Identity.Incarnation, blob, upload);
        var info = new BlobUploadInfo(blob, upload, 65_553, 0, 0, 0,
            new DateTimeOffset(begun.Operation.Timestamp).AddSeconds(3_600), BlobUploadStatus.Active, initial);
        await BlobPriorPublicationImage.ExactAsync(begun.Result.Get<BlobCommitResult<BlobUploadInfo>>().Value, info);
        await BlobPriorPublicationImage.ReceiptAsync(owner, begun.Result.Get<BlobCommitResult<BlobUploadInfo>>().Receipt, begun, blob);
        var commands = new List<BlobPriorPublicationCommand> { begun };
        var hash = initial;
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            var partBytes = bytes.AsMemory(ordinal * 65_536, ordinal == 0 ? 65_536 : 17);
            var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, ordinal, partBytes, BlobPriorPublicationOracle.Hash(partBytes.Span));
            var written = BlobPriorPublicationCommands.Send(owner, OperationKind.WriteBlobPart, part.CommandId, part);
            hash = BlobLaterPartOracle.Next(hash, ordinal, partBytes.Span);
            await BlobPriorPublicationImage.ExactAsync(written.Result.Get<BlobCommitResult<BlobUploadInfo>>().Value,
                info with
                {
                    NextOrdinal = ordinal + 1,
                    StoredBytes = ordinal == 0 ? 65_536 : 65_553,
                    IntegrityHash = hash
                });
            await BlobPriorPublicationImage.ReceiptAsync(owner, written.Result.Get<BlobCommitResult<BlobUploadInfo>>().Receipt, written, blob);
            commands.Add(written);
        }
        var request = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var completed = BlobPriorPublicationCommands.Send(owner, OperationKind.CompleteBlobUpload, request.CommandId, request);
        commands.Add(completed);
        var metadata = new BlobMetadata(blob, 1, upload, 65_553, 2, hash, new RowAccess(), new DateTimeOffset(completed.Operation.Timestamp));
        await BlobPriorPublicationImage.ExactAsync(completed.Result.Get<BlobCommitResult<BlobMetadata>>().Value, metadata);
        await BlobPriorPublicationImage.ReceiptAsync(owner, completed.Result.Get<BlobCommitResult<BlobMetadata>>().Receipt, completed, blob);
        var incarnation = owner.Store.Identity.Incarnation;
        var state = new BlobState(1, incarnation, incarnation, begin.CommandId, "root", blob, upload, new RowAccess(),
            65_553, 0, info.ExpiresAt, 2, 65_553, hash, BlobUploadStatus.Complete, 0, 0);
        var result = new BlobPriorPublicationState(blob, upload, bytes, metadata, state, commands.ToArray(), identity, principal);
        await BlobLaterPartAssertions.PublishedAsync(owner, result);
        return result;
    }
}
