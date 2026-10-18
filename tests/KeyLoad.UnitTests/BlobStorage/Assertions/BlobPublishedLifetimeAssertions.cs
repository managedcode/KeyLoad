using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobPublishedLifetimeAssertions
{
    internal static async Task Refuse(DatabaseEngine owner, ZoneTreeStore store, BlobRangeIntegrityFixture fixture,
        DeleteBlobRequest delete, ReclaimBlobRequest reclaim)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var metadata = Assert.ThrowsExactly<KeyLoadException>(() => new BlobStorageOperations(owner).Metadata("root", new(fixture.Blob)));
        await Assert.That(metadata.Code).IsEqualTo(ErrorCode.Corruption);
        var deletion = Assert.ThrowsExactly<KeyLoadException>(() => BlobPublishedLifetimeOperations.Send(owner, OperationKind.DeleteBlob, delete.CommandId, delete));
        await Assert.That(deletion.Code).IsEqualTo(ErrorCode.Corruption);
        var cleanup = Assert.ThrowsExactly<KeyLoadException>(() => BlobPublishedLifetimeOperations.Send(owner, OperationKind.ReclaimBlob, reclaim.CommandId, reclaim));
        await Assert.That(cleanup.Code).IsEqualTo(ErrorCode.Corruption);
        var replay = owner.ResolveOutcome(BlobPublishedLifetimeOperations.Original(owner, fixture.Commands[3]));
        await Assert.That(Errors.Code(replay.Problem)).IsEqualTo(ErrorCode.Corruption);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    internal static async Task Replay(DatabaseEngine owner, ZoneTreeStore store, BlobRangeRetainedOperation command)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        await BlobWholeFlowAssertions.Exact(owner.ResolveOutcome(BlobPublishedLifetimeOperations.Original(owner, command)), command.Original);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    internal static async Task Invalidated(DatabaseEngine owner, ZoneTreeStore store, BlobRangeRetainedOperation command)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var result = owner.ResolveOutcome(BlobPublishedLifetimeOperations.Original(owner, command));
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.TokenInvalidated);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    internal static async Task Empty(DatabaseEngine owner, ZoneTreeStore store, BlobRangeIntegrityFixture old,
        BlobPublishedEmptyLifetime current, BlobRangeRetainedOperation reclaimed)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var operations = new BlobStorageOperations(owner);
        await BlobWholeFlowAssertions.Exact(operations.Metadata("root", new(old.Blob)), current.Metadata);
        await BlobWholeFlowAssertions.Exact(operations.Read("root", new(old.Blob, 3, 0, 0)),
            new BlobReadResult(current.Metadata, 0, ReadOnlyMemory<byte>.Empty));
        await BlobWholeFlowAssertions.Exact(operations.UploadInfo("root", new(old.Blob, old.UploadId)),
            current.Active with
            {
                Status = BlobUploadStatus.Complete
            });
        await BlobWholeFlowAssertions.Quotas(owner, old.Blob, 0, 1, 0);
        var incarnation = store.Identity.Incarnation;
        var expectedState = new BlobState(1, incarnation, incarnation, current.Begin.CommandId, "root", old.Blob,
            old.UploadId, new RowAccess(), 0, 2, current.Active.ExpiresAt, 0, 0, current.Metadata.IntegrityHash!,
            BlobUploadStatus.Complete, 0, 0);
        await BlobWholeFlowAssertions.Exact(store.Read(view => view.GetRecord<BlobState>(
            BlobRangeIntegrityOracle.Key("blob-state-v1", old))), expectedState);
        await Replay(owner, store, current.Begin);
        await Replay(owner, store, current.Complete);
        await Invalidated(owner, store, old.Commands[0]);
        await Invalidated(owner, store, old.Commands[3]);
        await Invalidated(owner, store, reclaimed);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }
}
