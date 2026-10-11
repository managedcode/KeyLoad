using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobPublishedLifetimeCleanup
{
    internal static async Task<BlobRangeRetainedOperation> DeleteAndReclaim(DatabaseEngine owner, ZoneTreeStore store,
        BlobRangeIntegrityFixture fixture, DeleteBlobRequest deletion, ReclaimBlobRequest cleanup)
    {
        var deletedAt = owner.EvaluationClock.GetUtcNow();
        var deleted = BlobPublishedLifetimeOperations.Send(owner, OperationKind.DeleteBlob, deletion.CommandId, deletion, deletedAt);
        var tombstone = deleted.Original.Get<BlobCommitResult<BlobMetadata>>().Value;
        await BlobWholeFlowAssertions.Exact(tombstone, fixture.Metadata with
        { Revision = 2, VersionId = null, Length = 0, PartCount = 0, IntegrityHash = null, Deleted = true, UpdatedAt = deletedAt });
        await BlobWholeFlowAssertions.Receipt(owner, deleted.Original.Get<BlobCommitResult<BlobMetadata>>().Receipt, deletion.CommandId, fixture.Blob);
        await BlobWholeFlowAssertions.Quotas(owner, fixture.Blob, 65_553, 1, 0);
        var state = owner.Store.Read(view => view.GetRecord<BlobState>(BlobRangeIntegrityOracle.Key("blob-state-v1", fixture)));
        var incarnation = store.Identity.Incarnation;
        var expectedState = new BlobState(1, incarnation, incarnation, fixture.BeginCommandId, "root", fixture.Blob,
            fixture.UploadId, new RowAccess(), 65_553, 0, fixture.BeganAt.AddSeconds(3_600), 2, 65_553,
            fixture.Metadata.IntegrityHash!, BlobUploadStatus.Complete, 0, 0, true);
        await BlobWholeFlowAssertions.Exact(state, expectedState);
        await BlobWholeFlowAssertions.Exact(new BlobStorageOperations(owner).Metadata("root", new(fixture.Blob)), tombstone);
        await BlobPublishedLifetimeAssertions.Replay(owner, store, deleted);
        var reclaimed = BlobPublishedLifetimeOperations.Send(owner, OperationKind.ReclaimBlob, cleanup.CommandId, cleanup);
        var result = reclaimed.Original.Get<BlobCommitResult<BlobReclaimResult>>();
        await BlobWholeFlowAssertions.Exact(result.Value, new BlobReclaimResult(fixture.UploadId, 2, 0, 65_553, true));
        await BlobWholeFlowAssertions.Receipt(owner, result.Receipt, cleanup.CommandId, fixture.Blob);
        await BlobWholeFlowAssertions.Quotas(owner, fixture.Blob, 0, 0, 0);
        await Assert.That(new BlobStorageOperations(owner).UploadInfo("root", new(fixture.Blob, fixture.UploadId))).IsNull();
        foreach (var prefix in new[] { "blob-part-v1", "blob-partmeta-v1" })
        {
            for (var ordinal = 0; ordinal < 2; ordinal++)
            { await Assert.That(store.Read(view => view.ReadOwnedValue(BlobRangeIntegrityOracle.Key(prefix, fixture, ordinal)))).IsNull(); }
        }
        await BlobPublishedLifetimeAssertions.Replay(owner, store, reclaimed);
        await BlobPublishedLifetimeAssertions.Invalidated(owner, store, fixture.Commands[3]);
        return reclaimed;
    }
}
