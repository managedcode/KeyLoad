using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobCompleteCasPublicReplay
{
    internal static async Task UploadAsync(RequestCqrsRf3Callers callers, BlobCompleteCasPublicUpload upload,
        BlobCompleteCasPublicUpload[] uploads, BlobCompleteCasPublicUpload head, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(upload.Begun, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(upload.Begin, token)));
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await SqlRf3Protocol.EqualAsync(upload.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_begin_upload", upload.Begin, token))).Value);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await SqlRf3Protocol.EqualAsync(upload.Written, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.WriteBlobPartAsync(upload.Part, token)));
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await SqlRf3Protocol.EqualAsync(upload.Written, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_write_part", upload.Part, token))).Value);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        if (upload.Completed is { } completed)
        {
            await SqlRf3Protocol.EqualAsync(completed, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(upload.Complete, token)));
            await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
            await SqlRf3Protocol.EqualAsync(completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(await callers.Mcp.CallAsync("keyload_blobs_complete_upload", upload.Complete, token))).Value);
            await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        }
    }

    internal static async Task RemoveAsync(RequestCqrsRf3Callers callers, BlobCompleteCasPublicUpload loser,
        BlobCompleteCasPublicUpload[] uploads, BlobCompleteCasPublicUpload head, CancellationToken token)
    {
        loser.Abort = new(Guid.NewGuid(), loser.Begin.Blob, loser.Begin.UploadId);
        loser.Aborted = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_abort_upload", loser.Abort, token))).Value;
        loser.Current = loser.Written.Value with
        {
            Status = BlobUploadStatus.Aborted
        };
        await SqlRf3Protocol.EqualAsync(loser.Current, loser.Aborted.Value);
        await BlobCompleteCasPublicAssertions.ReceiptAsync(callers, loser.Aborted.Receipt, loser.Abort.CommandId, loser.Begin.Blob, token);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await SqlRf3Protocol.EqualAsync(loser.Aborted, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AbortBlobUploadAsync(loser.Abort, token)));
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await SqlRf3Protocol.EqualAsync(loser.Aborted, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_abort_upload", loser.Abort, token))).Value);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        loser.Reclaim = new(Guid.NewGuid(), loser.Begin.Blob, loser.Begin.UploadId, 1);
        loser.Reclaimed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReclaimBlobAsync(loser.Reclaim, token));
        await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(loser.Begin.UploadId, 1, 0, 2L, true), loser.Reclaimed.Value);
        await BlobCompleteCasPublicAssertions.ReceiptAsync(callers, loser.Reclaimed.Receipt, loser.Reclaim.CommandId, loser.Begin.Blob, token);
        loser.Current = null;
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await ReclaimAsync(callers, loser, uploads, head, token);
        await RemovedAsync(callers, loser, uploads, head, token);
    }

    internal static async Task ReclaimAsync(RequestCqrsRf3Callers callers, BlobCompleteCasPublicUpload loser,
        BlobCompleteCasPublicUpload[] uploads, BlobCompleteCasPublicUpload head, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(loser.Reclaimed, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReclaimBlobAsync(loser.Reclaim, token)));
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await SqlRf3Protocol.EqualAsync(loser.Reclaimed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobReclaimResult>>(await callers.Mcp.CallAsync("keyload_blobs_reclaim", loser.Reclaim, token))).Value);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
    }

    private static async Task RemovedAsync(RequestCqrsRf3Callers callers, BlobCompleteCasPublicUpload loser,
        BlobCompleteCasPublicUpload[] uploads, BlobCompleteCasPublicUpload head, CancellationToken token)
    {
        await KeyLoad.IntegrationTests.Features.BackupRestore.ClusterRestoreRf3AuthorityTrial.ErrorAsync(await callers.Sdk.BeginBlobUploadAsync(loser.Begin, token), ErrorCode.TokenInvalidated);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_begin_upload", loser.Begin, token), ErrorCode.TokenInvalidated, dispatched: true);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await KeyLoad.IntegrationTests.Features.BackupRestore.ClusterRestoreRf3AuthorityTrial.ErrorAsync(await callers.Sdk.WriteBlobPartAsync(loser.Part, token), ErrorCode.TokenInvalidated);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_write_part", loser.Part, token), ErrorCode.TokenInvalidated, dispatched: true);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await KeyLoad.IntegrationTests.Features.BackupRestore.ClusterRestoreRf3AuthorityTrial.ErrorAsync(await callers.Sdk.AbortBlobUploadAsync(loser.Abort, token), ErrorCode.TokenInvalidated);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_abort_upload", loser.Abort, token), ErrorCode.TokenInvalidated, dispatched: true);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
    }
}
