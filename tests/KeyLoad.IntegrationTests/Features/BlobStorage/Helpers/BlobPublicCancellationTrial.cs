using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPublicCancellationTrial
{
    internal static async Task RunAsync(BlobPublicCancellationOwner owner, bool useMcp, CancellationToken token)
    {
        var callers = owner.Caller;
        var blob = new BlobRef(owner.Identity.Partition, BlobPublicCancellationSetup.Resource, "cancel-part-complete");
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(), 4, 0);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(begin, token));
        await BlobPublicCancellationAssertions.StateAsync(callers, blob, begun.Value, token);
        byte[] bytes = [17, 29, 43, 61];
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, begin.UploadId, 0, bytes, BlobIntegrity.PartHash(bytes));
        await BlobPublicCancellationHeld.RunAsync(owner, useMcp, part.CommandId,
            ct => callers.Sdk.WriteBlobPartAsync(part, ct),
            ct => BlobPublicCancellationHeld.CallAsync(callers, "keyload_blobs_write_part", part, ct),
            () => BlobPublicCancellationAssertions.StateAsync(callers, blob, begun.Value, token), token);
        var written = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.WriteBlobPartAsync(part, token));
        await BlobPublicCancellationReplay.PartAsync(callers, part, written, token);
        await Assert.That(written.Value.NextOrdinal).IsEqualTo(1);
        await Assert.That(written.Value.StoredBytes).IsEqualTo(4L);
        var chain = BlobIntegrity.NextHash(begun.Value.IntegrityHash, 0, bytes.Length, part.Sha256);
        await Assert.That(written.Value.IntegrityHash).IsEqualTo(chain);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, begin.UploadId, chain);
        await BlobPublicCancellationHeld.RunAsync(owner, useMcp, complete.CommandId,
            ct => callers.Sdk.CompleteBlobUploadAsync(complete, ct),
            ct => BlobPublicCancellationHeld.CallAsync(callers, "keyload_blobs_complete_upload", complete, ct),
            () => BlobPublicCancellationAssertions.StateAsync(callers, blob, written.Value, token), token);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(complete, token));
        await BlobPublicCancellationAssertions.CurrentAsync(callers, completed.Value, bytes, token);
        await BlobPublicCancellationReplay.CompleteAsync(callers, complete, completed, token);
        await BlobPublicCancellationAssertions.CurrentAsync(callers, completed.Value, bytes, token);
        await BlobPublicCancellationReplay.OriginalAsync(callers, begin, begun, part, written, complete, completed,
            () => BlobPublicCancellationAssertions.CurrentAsync(callers, completed.Value, bytes, token), token);
        var fresh = await FreshAsync(callers, blob, token);
        await BlobPublicCancellationAssertions.CurrentAsync(callers, fresh.Value, [83], token);
        await BlobPublicCancellationReplay.OriginalAsync(callers, begin, begun, part, written, complete, completed,
            () => BlobPublicCancellationAssertions.CurrentAsync(callers, fresh.Value, [83], token), token);
        await BlobPartialReadCancellationR45Trial.RunAsync(owner, useMcp, fresh.Value, token);
    }

    private static async Task<BlobCommitResult<BlobMetadata>> FreshAsync(RequestCqrsRf3Callers callers,
        BlobRef blob, CancellationToken token)
    {
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(), 1, 1);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(begin, token));
        byte[] bytes = [83];
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, begin.UploadId, 0, bytes, BlobIntegrity.PartHash(bytes));
        _ = await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_write_part", part, token));
        var hash = BlobIntegrity.NextHash(begun.Value.IntegrityHash, 0, 1, part.Sha256);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, begin.UploadId, hash);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(complete, token));
        await Assert.That(completed.Value.Revision).IsEqualTo(2L);
        return completed;
    }
}
