using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobReclaimHealthyContinuation
{
    internal static async Task Run(DatabaseEngine database, ZoneTreeStore store, BlobReclaimWholeFlowFixture fixture,
        ReclaimBlobRequest first, ReclaimBlobRequest final)
    {
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), fixture.Blob, fixture.UploadId,
            BlobReclaimWholeFlowFixture.Replacement.Length, BlobReclaimWholeFlowFixture.InitialRevision);
        var begun = BlobReclaimWholeFlow.Apply(database, OperationKind.BeginBlobUpload, begin, begin.CommandId)
            .Get<BlobCommitResult<BlobUploadInfo>>();
        await BlobWholeFlowAssertions.Receipt(database, begun.Receipt, begin.CommandId, fixture.Blob);
        await BlobWholeFlowAssertions.Quotas(database, fixture.Blob, BlobReclaimWholeFlowFixture.Replacement.Length,
            BlobReclaimWholeFlowFixture.SingleCount, BlobReclaimWholeFlowFixture.SingleCount);
        await RefusedOldLifetime(database, store, OperationKind.AbortBlobUpload, fixture.Abort, fixture.Abort.CommandId);
        await RefusedOldLifetime(database, store, OperationKind.ReclaimBlob, first, first.CommandId);
        await RefusedOldLifetime(database, store, OperationKind.ReclaimBlob, final, final.CommandId);
        await BlobWholeFlowAssertions.Exact(new BlobStorageOperations(database).UploadInfo(
            BlobReclaimWholeFlowFixture.Principal, new(fixture.Blob, fixture.UploadId)), begun.Value);
        var part = new WriteBlobPartRequest(Guid.NewGuid(), fixture.Blob, fixture.UploadId,
            BlobReclaimWholeFlowFixture.FirstOrdinal, BlobReclaimWholeFlowFixture.Replacement,
            BlobIntegrity.PartHash(BlobReclaimWholeFlowFixture.Replacement));
        var written = BlobReclaimWholeFlow.Apply(database, OperationKind.WriteBlobPart, part, part.CommandId)
            .Get<BlobCommitResult<BlobUploadInfo>>();
        var chain = BlobIntegrity.NextHash(begun.Value.IntegrityHash, part.Ordinal, part.Bytes.Length, part.Sha256);
        await Assert.That(written.Value.IntegrityHash).IsEqualTo(chain);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), fixture.Blob, fixture.UploadId, chain);
        var now = database.EvaluationClock.GetUtcNow();
        var result = QueueWholeFlowStorage.Apply(database, OperationKind.CompleteBlobUpload, complete, complete.CommandId, now);
        var published = result.Get<BlobCommitResult<BlobMetadata>>();
        var expected = new BlobMetadata(fixture.Blob, BlobReclaimWholeFlowFixture.PublishedRevision,
            fixture.UploadId, part.Bytes.Length, BlobReclaimWholeFlowFixture.SingleCount, chain, new RowAccess(), now);
        await BlobWholeFlowAssertions.Exact(published.Value, expected);
        await BlobWholeFlowAssertions.Receipt(database, published.Receipt, complete.CommandId, fixture.Blob);
        await BlobWholeFlowAssertions.Quotas(database, fixture.Blob, part.Bytes.Length,
            BlobReclaimWholeFlowFixture.SingleCount, BlobReclaimWholeFlowFixture.EmptyCount);
        await BlobReclaimWholeFlow.Replay(database, store, OperationKind.CompleteBlobUpload, complete, complete.CommandId, result);
        await Ranges(database, store, expected);
    }

    private static async Task RefusedOldLifetime<T>(DatabaseEngine database, ZoneTreeStore store,
        OperationKind kind, T request, Guid command)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        var result = BlobReclaimWholeFlow.Apply(database, kind, request, command);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(result.SafeDetail).IsEqualTo(BlobErrors.Lifetime);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    private static async Task Ranges(DatabaseEngine database, ZoneTreeStore store, BlobMetadata expected)
    {
        var operations = new BlobStorageOperations(database);
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        await BlobWholeFlowAssertions.Exact(operations.Metadata(BlobReclaimWholeFlowFixture.Principal, new(expected.Blob)), expected);
        var full = operations.Read(BlobReclaimWholeFlowFixture.Principal,
            new(expected.Blob, expected.Revision, BlobReclaimWholeFlowFixture.FirstOrdinal, BlobReclaimWholeFlowFixture.Replacement.Length));
        await BlobWholeFlowAssertions.Exact(full, new BlobReadResult(expected, BlobReclaimWholeFlowFixture.FirstOrdinal,
            BlobReclaimWholeFlowFixture.Replacement));
        var partial = operations.Read(BlobReclaimWholeFlowFixture.Principal,
            new(expected.Blob, expected.Revision, BlobReclaimWholeFlowFixture.RangeOffset, BlobReclaimWholeFlowFixture.RangeCount));
        await BlobWholeFlowAssertions.Exact(partial, new BlobReadResult(expected, BlobReclaimWholeFlowFixture.RangeOffset,
            BlobReclaimWholeFlowFixture.Replacement.AsMemory(BlobReclaimWholeFlowFixture.RangeOffset, BlobReclaimWholeFlowFixture.RangeCount)));
        var zero = operations.Read(BlobReclaimWholeFlowFixture.Principal,
            new(expected.Blob, expected.Revision, expected.Length, BlobReclaimWholeFlowFixture.EmptyCount));
        await BlobWholeFlowAssertions.Exact(zero, new BlobReadResult(expected, expected.Length, ReadOnlyMemory<byte>.Empty));
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }
}
