using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobGlobalQuotaR49Operations
{
    internal static async Task BeginAsync(RequestCqrsRf3Callers callers, BlobGlobalQuotaR49State state, BlobGlobalQuotaR49Upload upload, BeginBlobUploadRequest request, CancellationToken token)
    {
        var seed = BlobGlobalQuotaR49Corpus.Initial(state.Placement.Incarnation, request);
        var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(request, token));
        var expected = new BlobUploadInfo(request.Blob, request.UploadId, request.Length, 0L, 0, 0L, result.Value.ExpiresAt, BlobUploadStatus.Active, seed);
        await Assert.That(Convert.FromHexString(expected.IntegrityHash).Length).IsEqualTo(32);
        await SqlRf3Protocol.EqualAsync(expected, result.Value);
        await BlobGlobalQuotaR49Results.ReceiptAsync(result.Receipt, request.CommandId, state);
        upload.AcceptedBegin = request;
        upload.Begun = result;
        upload.Progress = expected;
        await BlobGlobalQuotaR49Current.RunAsync(callers, state, token);
    }
    internal static async Task PartAsync(RequestCqrsRf3Callers callers, BlobGlobalQuotaR49State state, BlobGlobalQuotaR49Upload upload, int offset, int count, CancellationToken token)
    {
        var current = upload.Progress ?? throw new InvalidOperationException();
        var bytes = BlobGlobalQuotaR49Corpus.Bytes(offset, count);
        var request = new WriteBlobPartRequest(Guid.NewGuid(), upload.Blob, current.UploadId, upload.Parts.Count, bytes, BlobGlobalQuotaR49Corpus.PartHash(bytes));
        var result = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_write_part", request, token))).Value;
        var expected = current with
        {
            NextOrdinal = current.NextOrdinal + 1,
            StoredBytes = current.StoredBytes + count,
            IntegrityHash = BlobGlobalQuotaR49Corpus.Next(current.IntegrityHash, request.Ordinal, bytes)
        };
        await SqlRf3Protocol.EqualAsync(expected, result.Value);
        await BlobGlobalQuotaR49Results.ReceiptAsync(result.Receipt, request.CommandId, state);
        upload.Parts.Add(request);
        upload.Written.Add(result);
        upload.Progress = expected;
        await BlobGlobalQuotaR49Current.RunAsync(callers, state, token);
    }
    internal static async Task PublishAsync(RequestCqrsRf3Callers callers, BlobGlobalQuotaR49State state, BlobGlobalQuotaR49Upload upload, CancellationToken token)
    {
        await BeginAsync(callers, state, upload, upload.Begin, token);
        await PartAsync(callers, state, upload, 0, BlobGlobalQuotaR49Corpus.PartLength, token);
        await PartAsync(callers, state, upload, BlobGlobalQuotaR49Corpus.PartLength, 4, token);
        var progress = upload.Progress ?? throw new InvalidOperationException();
        var request = new CompleteBlobUploadRequest(Guid.NewGuid(), upload.Blob, progress.UploadId, progress.IntegrityHash);
        var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(request, token));
        var expected = new BlobMetadata(upload.Blob, 1L, progress.UploadId, BlobGlobalQuotaR49Corpus.SmallLength, 2,
            progress.IntegrityHash, new RowAccess(), result.Value.UpdatedAt, false);
        await SqlRf3Protocol.EqualAsync(expected, result.Value);
        await BlobGlobalQuotaR49Results.ReceiptAsync(result.Receipt, request.CommandId, state);
        upload.Complete = request;
        upload.Completed = result;
        upload.Metadata = expected;
        upload.Progress = progress with
        {
            Status = BlobUploadStatus.Complete
        };
        await BlobGlobalQuotaR49Current.RunAsync(callers, state, token);
    }
    internal static async Task AbortAsync(RequestCqrsRf3Callers callers, BlobGlobalQuotaR49State state, BlobGlobalQuotaR49Upload upload, CancellationToken token)
    {
        var current = upload.Progress ?? throw new InvalidOperationException();
        var request = new AbortBlobUploadRequest(Guid.NewGuid(), upload.Blob, current.UploadId);
        var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.AbortBlobUploadAsync(request, token));
        var expected = current with
        {
            Status = BlobUploadStatus.Aborted
        };
        await SqlRf3Protocol.EqualAsync(expected, result.Value);
        await BlobGlobalQuotaR49Results.ReceiptAsync(result.Receipt, request.CommandId, state);
        upload.Abort = request;
        upload.Aborted = result;
        upload.Progress = expected;
        await BlobGlobalQuotaR49Current.RunAsync(callers, state, token);
    }
    internal static async Task ReclaimAsync(RequestCqrsRf3Callers callers, BlobGlobalQuotaR49State state, BlobGlobalQuotaR49Upload upload, int parts, long released, CancellationToken token)
    {
        var current = upload.Progress ?? throw new InvalidOperationException();
        var request = new ReclaimBlobRequest(Guid.NewGuid(), upload.Blob, current.UploadId, 1);
        var result = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobReclaimResult>>(await callers.Mcp.CallAsync("keyload_blobs_reclaim", request, token))).Value;
        await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(current.UploadId, parts, 0, released, true), result.Value);
        await BlobGlobalQuotaR49Results.ReceiptAsync(result.Receipt, request.CommandId, state);
        upload.Reclaim = request;
        upload.Reclaimed = result;
        upload.Progress = null;
        upload.Removed = true;
        await BlobGlobalQuotaR49Current.RunAsync(callers, state, token);
    }
}
