using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobCompleteCasPublicPublication
{
    internal static async Task StageAsync(RequestCqrsRf3Callers callers, BlobCompleteCasPublicUpload upload,
        BlobCompleteCasPublicUpload[] uploads, BlobCompleteCasPublicUpload? head, CancellationToken token)
    {
        upload.Begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(upload.Begin, token));
        upload.Current = upload.Begun.Value;
        await Assert.That(upload.Begun.Value.Blob).IsEqualTo(upload.Begin.Blob);
        await Assert.That(upload.Begun.Value.UploadId).IsEqualTo(upload.Begin.UploadId);
        await Assert.That(upload.Begun.Value.ExpectedRevision).IsEqualTo(upload.Begin.ExpectedRevision);
        await Assert.That(upload.Begun.Value.DeclaredLength).IsEqualTo((long)upload.Bytes.Length);
        await Assert.That(upload.Begun.Value.StoredBytes).IsEqualTo(0L);
        await Assert.That(upload.Begun.Value.NextOrdinal).IsEqualTo(0);
        await Assert.That(upload.Begun.Value.Status).IsEqualTo(BlobUploadStatus.Active);
        await BlobCompleteCasPublicAssertions.ReceiptAsync(callers, upload.Begun.Receipt, upload.Begin.CommandId, upload.Begin.Blob, token);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        upload.Part = new(Guid.NewGuid(), upload.Begin.Blob, upload.Begin.UploadId, 0, upload.Bytes, upload.Sha256);
        upload.Written = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(await callers.Mcp.CallAsync("keyload_blobs_write_part", upload.Part, token))).Value;
        var chain = BlobIntegrity.NextHash(upload.Begun.Value.IntegrityHash, 0, upload.Bytes.Length, upload.Sha256);
        upload.Current = upload.Begun.Value with { StoredBytes = upload.Bytes.Length, NextOrdinal = 1, IntegrityHash = chain };
        await SqlRf3Protocol.EqualAsync(upload.Current, upload.Written.Value);
        await BlobCompleteCasPublicAssertions.ReceiptAsync(callers, upload.Written.Receipt, upload.Part.CommandId, upload.Begin.Blob, token);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, head, token);
        upload.Complete = new(Guid.NewGuid(), upload.Begin.Blob, upload.Begin.UploadId, chain);
    }

    internal static async Task CompleteAsync(RequestCqrsRf3Callers callers, BlobCompleteCasPublicUpload upload,
        BlobCompleteCasPublicUpload[] uploads, bool official, CancellationToken token)
    {
        upload.Completed = official
            ? (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(await callers.Mcp.CallAsync("keyload_blobs_complete_upload", upload.Complete, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(upload.Complete, token));
        upload.Current = upload.Written.Value with { Status = BlobUploadStatus.Complete };
        await Assert.That(upload.Completed.Value.Blob).IsEqualTo(upload.Begin.Blob);
        await Assert.That(upload.Completed.Value.VersionId).IsEqualTo(upload.Begin.UploadId);
        await Assert.That(upload.Completed.Value.Revision).IsEqualTo(upload.Begin.ExpectedRevision + 1L);
        await Assert.That(upload.Completed.Value.Length).IsEqualTo((long)upload.Bytes.Length);
        await Assert.That(upload.Completed.Value.PartCount).IsEqualTo(1);
        await Assert.That(upload.Completed.Value.Deleted).IsFalse();
        await Assert.That(upload.Completed.Value.IntegrityHash).IsEqualTo(upload.Complete.ExpectedIntegrityHash);
        await BlobCompleteCasPublicAssertions.ReceiptAsync(callers, upload.Completed.Receipt, upload.Complete.CommandId, upload.Begin.Blob, token);
        await BlobCompleteCasPublicAssertions.CurrentAsync(callers, uploads, upload, token);
    }
}
