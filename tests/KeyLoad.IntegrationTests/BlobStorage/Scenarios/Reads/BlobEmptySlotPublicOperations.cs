using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobEmptySlotPublicOperations
{
    internal static async Task PublishAsync(RequestCqrsRf3Callers callers, BlobEmptySlotPublicState state, CancellationToken token)
    {
        state.Healthy = state.RetainedRefused with
        {
            CommandId = Guid.NewGuid()
        };
        state.HealthyBegun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(state.Healthy, token));
        state.HealthyCurrent = state.HealthyBegun.Value;
        await Assert.That(state.HealthyBegun.Value.DeclaredLength).IsEqualTo(0L);
        await Assert.That(state.HealthyBegun.Value.StoredBytes).IsEqualTo(0L);
        await Assert.That(state.HealthyBegun.Value.NextOrdinal).IsEqualTo(0);
        await Assert.That(state.HealthyBegun.Value.Status).IsEqualTo(BlobUploadStatus.Active);
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
        state.Complete = new(Guid.NewGuid(), state.Blob, state.Healthy.UploadId, state.HealthyBegun.Value.IntegrityHash);
        state.Published = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(await callers.Mcp.CallAsync("keyload_blobs_complete_upload", state.Complete, token))).Value;
        state.HealthyCurrent = state.HealthyBegun.Value with
        {
            Status = BlobUploadStatus.Complete
        };
        await Assert.That(state.Published.Value.Blob).IsEqualTo(state.Blob);
        await Assert.That(state.Published.Value.VersionId).IsEqualTo(state.Healthy.UploadId);
        await Assert.That(state.Published.Value.Revision).IsEqualTo(1L);
        await Assert.That(state.Published.Value.Length).IsEqualTo(0L);
        await Assert.That(state.Published.Value.PartCount).IsEqualTo(0);
        await Assert.That(state.Published.Value.Deleted).IsFalse();
        await Assert.That(state.Published.Value.IntegrityHash).IsEqualTo(state.HealthyBegun.Value.IntegrityHash);
        await BlobEmptySlotPublicAssertions.ReceiptAsync(callers, state.HealthyBegun.Receipt, state.Healthy.CommandId, state.Blob, token);
        await BlobEmptySlotPublicAssertions.ReceiptAsync(callers, state.Published.Receipt, state.Complete.CommandId, state.Blob, token);
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
    }

    internal static async Task ReclaimReplayAsync(RequestCqrsRf3Callers callers, BlobEmptySlotPublicState state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Reclaimed, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReclaimBlobAsync(state.Reclaim, token)));
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(state.Reclaimed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobReclaimResult>>(await callers.Mcp.CallAsync("keyload_blobs_reclaim", state.Reclaim, token))).Value);
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers callers, BlobEmptySlotPublicState state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.HealthyBegun, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(state.Healthy, token)));
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(state.HealthyBegun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_begin_upload", state.Healthy, token))).Value);
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(state.Published, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(state.Complete, token)));
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(state.Published, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(await callers.Mcp.CallAsync("keyload_blobs_complete_upload", state.Complete, token))).Value);
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
        await ReclaimReplayAsync(callers, state, token);
        await BlobEmptySlotPublicAssertions.RefusedAsync(callers, state, state.InitialRefused, token);
        await BlobEmptySlotPublicAssertions.RefusedAsync(callers, state, state.RetainedRefused, token);
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await callers.Sdk.BeginBlobUploadAsync(state.First, token), ErrorCode.TokenInvalidated);
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_begin_upload", state.First, token), ErrorCode.TokenInvalidated, dispatched: true);
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
        await ClusterRestoreRf3AuthorityTrial.ErrorAsync(await callers.Sdk.AbortBlobUploadAsync(state.Abort, token), ErrorCode.TokenInvalidated);
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_abort_upload", state.Abort, token), ErrorCode.TokenInvalidated, dispatched: true);
        await BlobEmptySlotPublicAssertions.CurrentAsync(callers, state, token);
    }
}
