using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobActiveUploadSlotPublicOperations
{
    internal static async Task BeginAsync(RequestCqrsRf3Callers callers, BlobActiveUploadSlotPublicState state,
        BlobActiveUploadSlotPublicState[] states, bool official, CancellationToken token)
    {
        state.Begun = official
            ? (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_begin_upload", state.Admitted, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(state.Admitted, token));
        state.Current = state.Begun.Value;
        await BlobActiveUploadSlotPublicAssertions.BegunAsync(callers, state, token);
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
    }

    internal static async Task AbortAsync(RequestCqrsRf3Callers callers, BlobActiveUploadSlotPublicState state,
        BlobActiveUploadSlotPublicState[] states, CancellationToken token)
    {
        state.Abort = new(Guid.NewGuid(), state.Blob, state.Original.UploadId);
        state.Aborted = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_abort_upload", state.Abort, token))).Value;
        state.Current = state.Begun.Value with { Status = BlobUploadStatus.Aborted };
        await SqlRf3Protocol.EqualAsync(state.Current, state.Aborted.Value);
        await BlobActiveUploadSlotPublicAssertions.ReceiptAsync(callers, state.Aborted.Receipt, state.Abort.CommandId, state.Blob, token);
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
        await ReplayBeginAsync(callers, state, states, token);
        await SqlRf3Protocol.EqualAsync(state.Aborted, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AbortBlobUploadAsync(state.Abort, token)));
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
        await SqlRf3Protocol.EqualAsync(state.Aborted, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_abort_upload", state.Abort, token))).Value);
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
    }

    internal static async Task PublishAsync(RequestCqrsRf3Callers callers, BlobActiveUploadSlotPublicState state,
        BlobActiveUploadSlotPublicState[] states, CancellationToken token)
    {
        state.Admitted = state.Original with { CommandId = Guid.NewGuid() };
        await BeginAsync(callers, state, states, false, token);
        state.Complete = new(Guid.NewGuid(), state.Blob, state.Original.UploadId, state.Begun.Value.IntegrityHash);
        state.Published = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(await callers.Mcp.CallAsync("keyload_blobs_complete_upload", state.Complete, token))).Value;
        state.Current = state.Begun.Value with { Status = BlobUploadStatus.Complete };
        await Assert.That(state.Published.Value.Blob).IsEqualTo(state.Blob);
        await Assert.That(state.Published.Value.VersionId).IsEqualTo(state.Original.UploadId);
        await Assert.That(state.Published.Value.Revision).IsEqualTo(1L);
        await Assert.That(state.Published.Value.Length).IsEqualTo(0L);
        await Assert.That(state.Published.Value.PartCount).IsEqualTo(0);
        await Assert.That(state.Published.Value.Deleted).IsFalse();
        await Assert.That(state.Published.Value.IntegrityHash).IsEqualTo(state.Begun.Value.IntegrityHash);
        await BlobActiveUploadSlotPublicAssertions.ReceiptAsync(callers, state.Published.Receipt, state.Complete.CommandId, state.Blob, token);
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
        await ReplayBeginAsync(callers, state, states, token);
        await SqlRf3Protocol.EqualAsync(state.Published, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(state.Complete, token)));
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
        await SqlRf3Protocol.EqualAsync(state.Published, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(await callers.Mcp.CallAsync("keyload_blobs_complete_upload", state.Complete, token))).Value);
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
    }

    private static async Task ReplayBeginAsync(RequestCqrsRf3Callers callers, BlobActiveUploadSlotPublicState state,
        BlobActiveUploadSlotPublicState[] states, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Begun, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(state.Admitted, token)));
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
        await SqlRf3Protocol.EqualAsync(state.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_begin_upload", state.Admitted, token))).Value);
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
    }

    internal static async Task ReclaimAsync(RequestCqrsRf3Callers callers, BlobActiveUploadSlotPublicState state,
        BlobActiveUploadSlotPublicState[] states, CancellationToken token)
    {
        state.Reclaim = new(Guid.NewGuid(), state.Blob, state.Original.UploadId, 1);
        state.Reclaimed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReclaimBlobAsync(state.Reclaim, token));
        await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(state.Original.UploadId, 0, 0, 0L, true), state.Reclaimed.Value);
        await BlobActiveUploadSlotPublicAssertions.ReceiptAsync(callers, state.Reclaimed.Receipt, state.Reclaim.CommandId, state.Blob, token);
        state.Current = null;
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
        await SqlRf3Protocol.EqualAsync(state.Reclaimed, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReclaimBlobAsync(state.Reclaim, token)));
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
        await SqlRf3Protocol.EqualAsync(state.Reclaimed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobReclaimResult>>(await callers.Mcp.CallAsync("keyload_blobs_reclaim", state.Reclaim, token))).Value);
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
        await RemovedAsync(callers, state, states, token);
    }

    private static async Task RemovedAsync(RequestCqrsRf3Callers callers, BlobActiveUploadSlotPublicState state,
        BlobActiveUploadSlotPublicState[] states, CancellationToken token)
    {
        await KeyLoad.IntegrationTests.Features.BackupRestore.ClusterRestoreRf3AuthorityTrial.ErrorAsync(await callers.Sdk.BeginBlobUploadAsync(state.Admitted, token), ErrorCode.TokenInvalidated);
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_begin_upload", state.Admitted, token), ErrorCode.TokenInvalidated, dispatched: true);
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
        await KeyLoad.IntegrationTests.Features.BackupRestore.ClusterRestoreRf3AuthorityTrial.ErrorAsync(await callers.Sdk.AbortBlobUploadAsync(state.Abort, token), ErrorCode.TokenInvalidated);
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_abort_upload", state.Abort, token), ErrorCode.TokenInvalidated, dispatched: true);
        await BlobActiveUploadSlotPublicAssertions.CurrentAsync(callers, states, token);
    }
}
