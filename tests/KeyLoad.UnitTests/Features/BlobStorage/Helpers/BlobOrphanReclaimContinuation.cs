using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobOrphanReclaimContinuation
{
    private const int FaultAndRepairCommitCount = 2;

    internal static async Task RefusedRepairHealthy(DatabaseEngine database, ZoneTreeStore store,
        BlobReclaimWholeFlowFixture fixture)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        var key = BlobKeys.Part(fixture.Blob, fixture.UploadId, BlobReclaimWholeFlowFixture.FirstOrdinal);
        var original = BlobReclaimWholeFlowFixture.Payload.AsSpan(BlobReclaimWholeFlowFixture.FirstOrdinal,
            BlobLimits.RawPartBytes).ToArray();
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))).IsNull();
        await Assert.That(new BlobStorageOperations(database).UploadInfo(BlobReclaimWholeFlowFixture.Principal,
            new(fixture.Blob, fixture.UploadId))).IsNull();
        store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
        var damagedPosition = store.Position;
        var damagedImage = QueueWholeFlowStorage.Bytes(store);
        var reclaim = new ReclaimBlobRequest(Guid.NewGuid(), fixture.Blob, fixture.UploadId,
            BlobReclaimWholeFlowFixture.SingleCount);
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), fixture.Blob, fixture.UploadId,
            BlobReclaimWholeFlowFixture.Replacement.Length, BlobReclaimWholeFlowFixture.InitialRevision);
        await Corrupt(database, store, OperationKind.ReclaimBlob, reclaim, reclaim.CommandId,
            damagedPosition, damagedImage);
        await Corrupt(database, store, OperationKind.BeginBlobUpload, begin, begin.CommandId,
            damagedPosition, damagedImage);
        var observed = store.Read(view => view.ReadOwnedValue(key));
        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.AsSpan().SequenceEqual(original)).IsTrue();
        store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        await BlobWholeFlowAssertions.Unchanged(store, position + FaultAndRepairCommitCount, image);
        var healthy = new ReclaimBlobRequest(Guid.NewGuid(), fixture.Blob, fixture.UploadId,
            BlobReclaimWholeFlowFixture.SingleCount);
        var result = BlobReclaimWholeFlow.Apply(database, OperationKind.ReclaimBlob, healthy, healthy.CommandId);
        var committed = result.Get<BlobCommitResult<BlobReclaimResult>>();
        await BlobWholeFlowAssertions.Exact(committed.Value, new BlobReclaimResult(fixture.UploadId,
            BlobReclaimWholeFlowFixture.EmptyCount, BlobReclaimWholeFlowFixture.EmptyCount,
            BlobReclaimWholeFlowFixture.EmptyCount, true));
        await BlobWholeFlowAssertions.Receipt(database, committed.Receipt, healthy.CommandId, fixture.Blob);
        await BlobWholeFlowAssertions.Quotas(database, fixture.Blob, BlobReclaimWholeFlowFixture.EmptyCount,
            BlobReclaimWholeFlowFixture.EmptyCount, BlobReclaimWholeFlowFixture.EmptyCount);
        await BlobReclaimWholeFlow.Replay(database, store, OperationKind.ReclaimBlob, healthy, healthy.CommandId, result);
    }

    private static async Task Corrupt<T>(DatabaseEngine database, ZoneTreeStore store,
        OperationKind kind, T request, Guid command, long position, string[] image)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            BlobReclaimWholeFlow.Apply(database, kind, request, command));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(BlobErrors.Damaged);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    internal static async Task Cold(TestDatabase database, ZoneTreeStore store, BlobReclaimWholeFlowFixture fixture,
        ReclaimBlobRequest first, ReclaimBlobRequest final,
        (CompleteBlobUploadRequest Request, OperationResult Result, BlobMetadata Metadata) published)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        store.Dispose();
        using var second = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
            UnitExecutionOptions.PointCacheExecution());
        var owner = database.CreateConfiguredDatabase(second);
        await BlobWholeFlowAssertions.Unchanged(second, position, image);
        await BlobReclaimWholeFlow.Replay(owner, second, OperationKind.CompleteBlobUpload,
            published.Request, published.Request.CommandId, published.Result);
        await RefusedLifetime(owner, second, OperationKind.AbortBlobUpload, fixture.Abort, fixture.Abort.CommandId);
        await RefusedLifetime(owner, second, OperationKind.ReclaimBlob, first, first.CommandId);
        await RefusedLifetime(owner, second, OperationKind.ReclaimBlob, final, final.CommandId);
        var operations = new BlobStorageOperations(owner);
        await BlobWholeFlowAssertions.Exact(operations.Metadata(BlobReclaimWholeFlowFixture.Principal,
            new(fixture.Blob)), published.Metadata);
        await Range(operations, published.Metadata, BlobReclaimWholeFlowFixture.FirstOrdinal,
            BlobReclaimWholeFlowFixture.Replacement.Length);
        await Range(operations, published.Metadata, BlobReclaimWholeFlowFixture.RangeOffset,
            BlobReclaimWholeFlowFixture.RangeCount);
        await Range(operations, published.Metadata, BlobReclaimWholeFlowFixture.Replacement.Length,
            BlobReclaimWholeFlowFixture.EmptyCount);
        await BlobWholeFlowAssertions.Quotas(owner, fixture.Blob, BlobReclaimWholeFlowFixture.Replacement.Length,
            BlobReclaimWholeFlowFixture.SingleCount, BlobReclaimWholeFlowFixture.EmptyCount);
        await BlobWholeFlowAssertions.Unchanged(second, position, image);
    }

    private static async Task RefusedLifetime<T>(DatabaseEngine database, ZoneTreeStore store,
        OperationKind kind, T request, Guid command)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        var result = BlobReclaimWholeFlow.Apply(database, kind, request, command);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(result.SafeDetail).IsEqualTo(BlobErrors.Lifetime);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    private static async Task Range(BlobStorageOperations operations, BlobMetadata metadata, int offset, int count)
    {
        var actual = operations.Read(BlobReclaimWholeFlowFixture.Principal,
            new(metadata.Blob, metadata.Revision, offset, count));
        var expected = new BlobReadResult(metadata, offset,
            BlobReclaimWholeFlowFixture.Replacement.AsMemory(offset, count));
        await BlobWholeFlowAssertions.Exact(actual, expected);
    }
}
