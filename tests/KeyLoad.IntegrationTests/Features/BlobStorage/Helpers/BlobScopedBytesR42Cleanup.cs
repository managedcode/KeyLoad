using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobScopedBytesR42Cleanup
{
    internal static async Task RunAsync(RequestCqrsRf3Callers callers, BlobScopedBytesR42State[] states, CancellationToken token)
    {
        var state = states[0];
        state.Abort = new(Guid.NewGuid(), state.Blob, state.Begin.UploadId);
        state.Aborted = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AbortBlobUploadAsync(state.Abort, token));
        state.Current = state.Written[1].Value with { Status = BlobUploadStatus.Aborted };
        await SqlRf3Protocol.EqualAsync(state.Current, state.Aborted.Value);
        await BlobScopedBytesR42Assertions.ReceiptAsync(state.Aborted.Receipt, state.Abort.CommandId, state);
        await BlobScopedBytesR42Assertions.CurrentAsync(callers, states, token);
        Task Observe() => BlobScopedBytesR42Assertions.CurrentAsync(callers, states, token);
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_abort_upload", state.Abort, state.Aborted, ct => callers.Sdk.AbortBlobUploadAsync(state.Abort, ct), Observe, token);
        for (var index = 0; index < 2; index++)
        {
            state.Reclaims[index] = new(Guid.NewGuid(), state.Blob, state.Begin.UploadId, 1);
            state.Reclaimed[index] = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobReclaimResult>>(await callers.Mcp.CallAsync("keyload_blobs_reclaim", state.Reclaims[index], token))).Value;
            await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(state.Begin.UploadId, 1, index == 0 ? 1 : 0, index == 0 ? 65536L : 17L, index == 1), state.Reclaimed[index].Value);
            await BlobScopedBytesR42Assertions.ReceiptAsync(state.Reclaimed[index].Receipt, state.Reclaims[index].CommandId, state);
            state.Current = index == 0 ? state.Current! with { StoredBytes = 17L } : null;
            await BlobScopedBytesR42Assertions.CurrentAsync(callers, states, token);
            if (index == 0)
            {
                var request = state.Reclaims[0];
                await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_reclaim", request, state.Reclaimed[0], ct => callers.Sdk.ReclaimBlobAsync(request, ct), Observe, token);
            }
        }
    }
}
