using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobSupersessionPublication
{
    internal static readonly byte[] Payload = [0x00, 0xA1, 0x7F, 0x22, 0xFF, 0x09, 0x43, 0x18];

    internal static async Task<BlobSupersessionVersion> PublishAsync(DatabaseEngine owner,
        BlobRangeIntegrityFixture first, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), first.Blob, upload, Payload.Length, 1L);
        var begun = BlobPublishedLifetimeOperations.Send(owner, OperationKind.BeginBlobUpload, begin.CommandId, begin);
        await BlobWholeFlowAssertions.Receipt(owner, begun.Original.Get<BlobCommitResult<BlobUploadInfo>>().Receipt,
            begin.CommandId, first.Blob, begun.ExpectedToken, begun.PreviousStorePosition);
        var part = new WriteBlobPartRequest(Guid.NewGuid(), first.Blob, upload, 0, Payload,
            BlobIntegrity.PartHash(Payload));
        var written = BlobPublishedLifetimeOperations.Send(owner, OperationKind.WriteBlobPart, part.CommandId, part);
        await BlobWholeFlowAssertions.Receipt(owner, written.Original.Get<BlobCommitResult<BlobUploadInfo>>().Receipt,
            part.CommandId, first.Blob, written.ExpectedToken, written.PreviousStorePosition);
        var initial = BlobIntegrity.InitialHash(owner.Store.Identity.Incarnation, first.Blob, upload, Payload.Length);
        var hash = BlobIntegrity.NextHash(initial, 0, Payload.Length, BlobIntegrity.PartHash(Payload));
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), first.Blob, upload, hash);
        var publishedAt = owner.EvaluationClock.GetUtcNow();
        var completed = BlobPublishedLifetimeOperations.Send(owner, OperationKind.CompleteBlobUpload,
            complete.CommandId, complete, publishedAt);
        var expected = new BlobMetadata(first.Blob, 2L, upload, Payload.Length, 1, hash, new RowAccess(), publishedAt);
        await BlobWholeFlowAssertions.Exact(completed.Original.Get<BlobCommitResult<BlobMetadata>>().Value, expected);
        await BlobWholeFlowAssertions.Receipt(owner, completed.Original.Get<BlobCommitResult<BlobMetadata>>().Receipt,
            complete.CommandId, first.Blob, completed.ExpectedToken, completed.PreviousStorePosition);
        return new(expected, [begun, written, completed]);
    }
}
