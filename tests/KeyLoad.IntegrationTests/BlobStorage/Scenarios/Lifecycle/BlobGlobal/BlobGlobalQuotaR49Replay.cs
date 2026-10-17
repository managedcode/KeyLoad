using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobGlobalQuotaR49Replay
{
    internal static Task RefusedAsync(RequestCqrsRf3Callers callers, BlobGlobalQuotaR49State state, CancellationToken token)
        => BlobGlobalQuotaR49Results.FailureAsync(callers, "keyload_blobs_begin_upload", state.Attempt.Begin,
            ct => callers.Sdk.BeginBlobUploadAsync(state.Attempt.Begin, ct), ErrorCode.ResourceExhausted,
            "The blob reservation quota is exhausted.", state, token);
    internal static async Task UploadAsync(RequestCqrsRf3Callers callers, BlobGlobalQuotaR49State state, BlobGlobalQuotaR49Upload upload, CancellationToken token)
    {
        var begin = upload.AcceptedBegin ?? upload.Begin;
        if (upload.Removed)
        {
            await LostAsync(callers, "keyload_blobs_begin_upload", begin, ct => callers.Sdk.BeginBlobUploadAsync(begin, ct), state, token);
            foreach (var part in upload.Parts)
            {
                await LostAsync(callers, "keyload_blobs_write_part", part, ct => callers.Sdk.WriteBlobPartAsync(part, ct), state, token);
            }
            if (upload.Abort is { } abort)
            {
                await LostAsync(callers, "keyload_blobs_abort_upload", abort, ct => callers.Sdk.AbortBlobUploadAsync(abort, ct), state, token);
            }
        }
        else
        {
            await BlobGlobalQuotaR49Results.ReplayAsync(callers, "keyload_blobs_begin_upload", begin, upload.Begun!, ct => callers.Sdk.BeginBlobUploadAsync(begin, ct), state, token);
            for (var index = 0; index < upload.Parts.Count; index++)
            {
                var part = upload.Parts[index];
                await BlobGlobalQuotaR49Results.ReplayAsync(callers, "keyload_blobs_write_part", part, upload.Written[index], ct => callers.Sdk.WriteBlobPartAsync(part, ct), state, token);
            }
            if (upload.Complete is { } complete)
            {
                await BlobGlobalQuotaR49Results.ReplayAsync(callers, "keyload_blobs_complete_upload", complete, upload.Completed!, ct => callers.Sdk.CompleteBlobUploadAsync(complete, ct), state, token);
            }
            if (upload.Abort is { } abort)
            {
                await BlobGlobalQuotaR49Results.ReplayAsync(callers, "keyload_blobs_abort_upload", abort, upload.Aborted!, ct => callers.Sdk.AbortBlobUploadAsync(abort, ct), state, token);
            }
        }
        if (upload.Reclaim is { } reclaim)
        {
            await BlobGlobalQuotaR49Results.ReplayAsync(callers, "keyload_blobs_reclaim", reclaim, upload.Reclaimed!, ct => callers.Sdk.ReclaimBlobAsync(reclaim, ct), state, token);
        }
    }
    internal static async Task AllAsync(RequestCqrsRf3Callers callers, BlobGlobalQuotaR49State state, CancellationToken token)
    {
        await RefusedAsync(callers, state, token);
        foreach (var upload in state.CurrentUploads.Where(upload => state.ConfiguredResources.Contains(upload.Blob.Resource)))
        {
            await UploadAsync(callers, state, upload, token);
        }
        await BlobGlobalQuotaR49Current.WindowsAsync(callers, state, token);
    }
    private static Task LostAsync<T>(RequestCqrsRf3Callers callers, string tool, object request,
        Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> sdk, BlobGlobalQuotaR49State state, CancellationToken token)
        => BlobGlobalQuotaR49Results.FailureAsync(callers, tool, request, sdk, ErrorCode.TokenInvalidated,
            "The selected upload lifetime is unavailable.", state, token);
}
