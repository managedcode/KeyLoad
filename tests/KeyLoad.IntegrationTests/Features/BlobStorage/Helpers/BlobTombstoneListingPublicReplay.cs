using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobTombstoneListingPublicReplay
{
    internal static async Task FinalAsync(RequestCqrsRf3Callers callers,
        BlobTombstoneListingPublicState state, CancellationToken token)
    {
        Task Observe() => BlobTombstoneListingPublicAssertions.CurrentAsync(callers, state, token);
        await Observe();
        var stale = new BlobReadRequest(state.Current.Begin.Blob, 1L, 0L, 4);
        await BlobBoundedRetirementPublicAssertions.FailedAsync(await callers.Sdk.ReadBlobRangeAsync(stale, token), ErrorCode.RevisionConflict);
        await Observe();
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_read_range", stale, token), ErrorCode.RevisionConflict, dispatched: true);
        await Observe();
        foreach (var old in state.Retired)
        {
            await GoneAsync(callers, old.Original, Observe, token);
            await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_delete", old.Delete, old.Deleted,
                ct => callers.Sdk.DeleteBlobAsync(old.Delete, ct), Observe, token);
            await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_reclaim", old.Reclaim, old.Reclaimed,
                ct => callers.Sdk.ReclaimBlobAsync(old.Reclaim, ct), Observe, token);
        }
        await BlobBoundedRetirementPublicReplay.PublicationAsync(callers, state.First, Observe, token);
        await BlobBoundedRetirementPublicReplay.PublicationAsync(callers, state.Current, Observe, token);
        await RefusedAsync(callers, "keyload_blobs_begin_upload", state.Begin,
            ct => callers.Sdk.BeginBlobUploadAsync(state.Begin, ct), Observe, token);
        await RefusedAsync(callers, "keyload_blobs_abort_upload", state.Abort,
            ct => callers.Sdk.AbortBlobUploadAsync(state.Abort, ct), Observe, token);
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_reclaim", state.Reclaim, state.Reclaimed,
            ct => callers.Sdk.ReclaimBlobAsync(state.Reclaim, ct), Observe, token);
    }

    private static async Task GoneAsync(RequestCqrsRf3Callers callers,
        BlobBoundedRetirementPublicPublication.Published old, Func<Task> observe, CancellationToken token)
    {
        await RefusedAsync(callers, "keyload_blobs_begin_upload", old.Begin,
            ct => callers.Sdk.BeginBlobUploadAsync(old.Begin, ct), observe, token);
        foreach (var part in old.Parts)
        {
            await RefusedAsync(callers, "keyload_blobs_write_part", part,
            ct => callers.Sdk.WriteBlobPartAsync(part, ct), observe, token);
        }
        await RefusedAsync(callers, "keyload_blobs_complete_upload", old.Complete,
            ct => callers.Sdk.CompleteBlobUploadAsync(old.Complete, ct), observe, token);
    }

    private static async Task RefusedAsync<T>(RequestCqrsRf3Callers callers, string tool, object request,
        Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> invoke, Func<Task> observe, CancellationToken token)
    {
        await BlobBoundedRetirementPublicAssertions.FailedAsync(await invoke(token), ErrorCode.TokenInvalidated);
        await observe();
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(tool, request, token), ErrorCode.TokenInvalidated, dispatched: true);
        await observe();
    }
}
