using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobTombstoneListingPublicRetirement
{
    internal static async Task<BlobTombstoneListingPublicState.Retirement> CreateAsync(
        RequestCqrsRf3Callers callers, BlobRef blob, AtomicPartitionPlacementResolution placement, CancellationToken token)
    {
        var original = await BlobBoundedRetirementPublicPublication.CreateAsync(callers, blob, 0L, [3, 5, 7, 9], token);
        await BlobTombstoneListingPublicAssertions.PublicationReceiptsAsync(placement, original);
        Task Published() => BlobBoundedRetirementPublicAssertions.CurrentAsync(callers, original, token);
        await BlobBoundedRetirementPublicReplay.PublicationAsync(callers, original, Published, token);
        var delete = new DeleteBlobRequest(Guid.NewGuid(), blob, 1L);
        var deleted = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.DeleteBlobAsync(delete, token));
        var tombstone = original.Completed.Value with
        { Revision = 2L, VersionId = null, Length = 0L, PartCount = 0, IntegrityHash = null, Deleted = true, UpdatedAt = deleted.Value.UpdatedAt };
        await SqlRf3Protocol.EqualAsync(tombstone, deleted.Value);
        await BlobTombstoneListingPublicAssertions.ReceiptAsync(placement, delete.CommandId, deleted.Receipt);
        Task Retired() => BlobBoundedRetirementPublicAssertions.HeadAsync(callers, tombstone, token);
        await Retired();
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_delete", delete, deleted,
            ct => callers.Sdk.DeleteBlobAsync(delete, ct), Retired, token);
        var reclaim = new ReclaimBlobRequest(Guid.NewGuid(), blob, original.Begin.UploadId, 1);
        var reclaimed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReclaimBlobAsync(reclaim, token));
        await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(original.Begin.UploadId, 1, 0, 4L, true), reclaimed.Value);
        await BlobTombstoneListingPublicAssertions.ReceiptAsync(placement, reclaim.CommandId, reclaimed.Receipt);
        async Task Removed()
        {
            await Retired();
            await BlobBoundedRetirementPublicAssertions.UploadAsync(callers, blob, original.Begin.UploadId, null, token);
        }
        await Removed();
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_reclaim", reclaim, reclaimed,
            ct => callers.Sdk.ReclaimBlobAsync(reclaim, ct), Removed, token);
        return new(original, delete, deleted, tombstone, reclaim, reclaimed);
    }

    internal static async Task RemoveUnpublishedAsync(RequestCqrsRf3Callers callers,
        BlobTombstoneListingPublicState state, CancellationToken token)
    {
        Task Observe() => BlobTombstoneListingPublicAssertions.CurrentAsync(callers, state, token);
        state.Abort = new(Guid.NewGuid(), state.Begin.Blob, state.Begin.UploadId);
        state.Aborted = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AbortBlobUploadAsync(state.Abort, token));
        state.Unpublished = state.Begun.Value with { Status = BlobUploadStatus.Aborted };
        await SqlRf3Protocol.EqualAsync(state.Unpublished, state.Aborted.Value);
        await BlobTombstoneListingPublicAssertions.ReceiptAsync(state.Placement, state.Abort.CommandId, state.Aborted.Receipt);
        await Observe();
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_abort_upload", state.Abort, state.Aborted,
            ct => callers.Sdk.AbortBlobUploadAsync(state.Abort, ct), Observe, token);
        state.Reclaim = new(Guid.NewGuid(), state.Begin.Blob, state.Begin.UploadId, 1);
        state.Reclaimed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReclaimBlobAsync(state.Reclaim, token));
        await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(state.Begin.UploadId, 0, 0, 0L, true), state.Reclaimed.Value);
        await BlobTombstoneListingPublicAssertions.ReceiptAsync(state.Placement, state.Reclaim.CommandId, state.Reclaimed.Receipt);
        state.Unpublished = null;
        await Observe();
    }
}
