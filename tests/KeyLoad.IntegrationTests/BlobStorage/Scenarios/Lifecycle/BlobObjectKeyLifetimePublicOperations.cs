using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobObjectKeyLifetimePublicOperations
{
    internal static async Task BeginAsync(RequestCqrsClusterCallers callers, BlobObjectKeyLifetimePublicState state,
        BlobObjectKeyLifetimePublicState[] states, bool official, CancellationToken token)
    {
        state.Begun = official
            ? (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_begin_upload", state.Admitted, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(state.Admitted, token));
        state.Current = state.Begun.Value;
        await BlobObjectKeyLifetimePublicAssertions.BegunAsync(state);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
    }

    internal static async Task AbortAsync(RequestCqrsClusterCallers callers, BlobObjectKeyLifetimePublicState state,
        BlobObjectKeyLifetimePublicState[] states, CancellationToken token)
    {
        state.Abort = new(Guid.NewGuid(), state.Blob, state.Original.UploadId);
        state.Aborted = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_abort_upload", state.Abort, token))).Value;
        state.Current = state.Begun.Value with
        {
            Status = BlobUploadStatus.Aborted
        };
        await SqlClusterProtocol.EqualAsync(state.Current, state.Aborted.Value);
        await BlobObjectKeyLifetimePublicAssertions.ReceiptAsync(state.Aborted.Receipt, state.Abort.CommandId, state.Blob);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        await ReplayBeginAsync(callers, state, states, token);
        await SqlClusterProtocol.EqualAsync(state.Aborted, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AbortBlobUploadAsync(state.Abort, token)));
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        await SqlClusterProtocol.EqualAsync(state.Aborted, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_abort_upload", state.Abort, token))).Value);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
    }

    internal static async Task PublishAsync(RequestCqrsClusterCallers callers, BlobObjectKeyLifetimePublicState state,
        BlobObjectKeyLifetimePublicState[] states, CancellationToken token)
    {
        state.Admitted = state.Original with
        {
            CommandId = Guid.NewGuid()
        };
        await BeginAsync(callers, state, states, false, token);
        state.Complete = new(Guid.NewGuid(), state.Blob, state.Original.UploadId, state.Begun.Value.IntegrityHash);
        state.Published = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(await callers.Mcp.CallAsync("keyload_blobs_complete_upload", state.Complete, token))).Value;
        state.Current = state.Begun.Value with
        {
            Status = BlobUploadStatus.Complete
        };
        await Assert.That(state.Published.Value.Blob).IsEqualTo(state.Blob);
        await Assert.That(state.Published.Value.VersionId).IsEqualTo(state.Original.UploadId);
        await Assert.That(state.Published.Value.Revision).IsEqualTo(1L);
        await Assert.That(state.Published.Value.Length).IsEqualTo(0L);
        await Assert.That(state.Published.Value.PartCount).IsEqualTo(0);
        await Assert.That(state.Published.Value.Deleted).IsFalse();
        await Assert.That(state.Published.Value.IntegrityHash).IsEqualTo(state.Begun.Value.IntegrityHash);
        await BlobObjectKeyLifetimePublicAssertions.ReceiptAsync(state.Published.Receipt, state.Complete.CommandId, state.Blob);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        await ReplayBeginAsync(callers, state, states, token);
        await SqlClusterProtocol.EqualAsync(state.Published, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(state.Complete, token)));
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        await SqlClusterProtocol.EqualAsync(state.Published, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(await callers.Mcp.CallAsync("keyload_blobs_complete_upload", state.Complete, token))).Value);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
    }

    private static async Task ReplayBeginAsync(RequestCqrsClusterCallers callers, BlobObjectKeyLifetimePublicState state,
        BlobObjectKeyLifetimePublicState[] states, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(state.Begun, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(state.Admitted, token)));
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        await SqlClusterProtocol.EqualAsync(state.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_begin_upload", state.Admitted, token))).Value);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
    }

    internal static async Task ReclaimAsync(RequestCqrsClusterCallers callers, BlobObjectKeyLifetimePublicState state,
        BlobObjectKeyLifetimePublicState[] states, CancellationToken token)
    {
        state.Reclaim = new(Guid.NewGuid(), state.Blob, state.Original.UploadId, 1);
        state.Reclaimed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReclaimBlobAsync(state.Reclaim, token));
        await SqlClusterProtocol.EqualAsync(new BlobReclaimResult(state.Original.UploadId, 0, 0, 0L, true), state.Reclaimed.Value);
        await BlobObjectKeyLifetimePublicAssertions.ReceiptAsync(state.Reclaimed.Receipt, state.Reclaim.CommandId, state.Blob);
        state.Current = null;
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        await SqlClusterProtocol.EqualAsync(state.Reclaimed, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReclaimBlobAsync(state.Reclaim, token)));
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        await SqlClusterProtocol.EqualAsync(state.Reclaimed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobReclaimResult>>(await callers.Mcp.CallAsync("keyload_blobs_reclaim", state.Reclaim, token))).Value);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        await RemovedAsync(callers, state, states, token);
    }

    private static async Task RemovedAsync(RequestCqrsClusterCallers callers, BlobObjectKeyLifetimePublicState state,
        BlobObjectKeyLifetimePublicState[] states, CancellationToken token)
    {
        await BlobBoundedRetirementPublicAssertions.FailedAsync(await callers.Sdk.BeginBlobUploadAsync(state.Admitted, token), ErrorCode.TokenInvalidated);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_begin_upload", state.Admitted, token), ErrorCode.TokenInvalidated, dispatched: true);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        await BlobBoundedRetirementPublicAssertions.FailedAsync(await callers.Sdk.AbortBlobUploadAsync(state.Abort, token), ErrorCode.TokenInvalidated);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_abort_upload", state.Abort, token), ErrorCode.TokenInvalidated, dispatched: true);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
    }
}
