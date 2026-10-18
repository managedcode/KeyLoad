using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobScopedBytesR42Replay
{
    internal static async Task RunAsync(RequestCqrsClusterCallers callers, BlobScopedBytesR42State[] states, CancellationToken token)
    {
        Task Observe() => BlobScopedBytesR42Assertions.CurrentAsync(callers, states, token);
        foreach (var state in states.Skip(1).Take(3))
        {
            await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_begin_upload", state.Begin, state.Begun, ct => callers.Sdk.BeginBlobUploadAsync(state.Begin, ct), Observe, token);
            for (var index = 0; index < 2; index++)
            {
                var part = state.Parts[index];
                await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_write_part", part, state.Written[index], ct => callers.Sdk.WriteBlobPartAsync(part, ct), Observe, token);
            }
            await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_complete_upload", state.Complete, state.Published!, ct => callers.Sdk.CompleteBlobUploadAsync(state.Complete, ct), Observe, token);
        }
        var removed = states[0];
        var final = removed.Reclaims[1];
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_reclaim", final, removed.Reclaimed[1], ct => callers.Sdk.ReclaimBlobAsync(final, ct), Observe, token);
        await RemovedAsync(callers, removed, states, token);
    }
    private static async Task RemovedAsync(RequestCqrsClusterCallers callers, BlobScopedBytesR42State state, BlobScopedBytesR42State[] states, CancellationToken token)
    {
        await RefuseAsync(callers, state, state.Begin.CommandId, "keyload_blobs_begin_upload", state.Begin, ct => callers.Sdk.BeginBlobUploadAsync(state.Begin, ct), states, token);
        foreach (var part in state.Parts)
        {
            await RefuseAsync(callers, state, part.CommandId, "keyload_blobs_write_part", part, ct => callers.Sdk.WriteBlobPartAsync(part, ct), states, token);
        }
        await RefuseAsync(callers, state, state.Abort.CommandId, "keyload_blobs_abort_upload", state.Abort, ct => callers.Sdk.AbortBlobUploadAsync(state.Abort, ct), states, token);
        var partial = state.Reclaims[0];
        await RefuseAsync(callers, state, partial.CommandId, "keyload_blobs_reclaim", partial, ct => callers.Sdk.ReclaimBlobAsync(partial, ct), states, token);
    }
    private static async Task RefuseAsync<T>(RequestCqrsClusterCallers callers, BlobScopedBytesR42State state, Guid id, string tool, object request,
     Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> invoke, BlobScopedBytesR42State[] states, CancellationToken token)
    {
        var result = await invoke(token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        var closed = Errors.Problem(ErrorCode.TokenInvalidated, "The selected upload lifetime is unavailable.");
        await SqlClusterProtocol.EqualAsync(closed, result.Problem);
        if (state.Refusals.TryGetValue(id, out var original))
        {
            await SqlClusterProtocol.EqualAsync(original, result.Problem);
        }
        else
        {
            state.Refusals.Add(id, result.Problem!);
        }
        await BlobScopedBytesR42Assertions.CurrentAsync(callers, states, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(tool, request, token), ErrorCode.TokenInvalidated, dispatched: true);
        await BlobScopedBytesR42Assertions.CurrentAsync(callers, states, token);
    }
}
