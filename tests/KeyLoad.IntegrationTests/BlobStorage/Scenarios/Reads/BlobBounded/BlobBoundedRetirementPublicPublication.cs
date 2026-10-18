using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobBoundedRetirementPublicPublication
{
    internal sealed record Published(BeginBlobUploadRequest Begin, BlobCommitResult<BlobUploadInfo> Begun,
        WriteBlobPartRequest[] Parts, BlobCommitResult<BlobUploadInfo>[] Written,
        CompleteBlobUploadRequest Complete, BlobCommitResult<BlobMetadata> Completed, byte[] Bytes);

    internal static async Task<Published> CreateAsync(RequestCqrsClusterCallers callers, BlobRef blob,
        long expectedRevision, byte[] bytes, CancellationToken token)
    {
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(), bytes.LongLength, expectedRevision);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(begin, token));
        var parts = new List<WriteBlobPartRequest>();
        var written = new List<BlobCommitResult<BlobUploadInfo>>();
        var hash = begun.Value.IntegrityHash;
        for (var offset = 0; offset < bytes.Length; offset += BlobLimits.RawPartBytes)
        {
            var payload = bytes.AsMemory(offset, Math.Min(BlobLimits.RawPartBytes, bytes.Length - offset));
            var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, begin.UploadId, parts.Count,
                payload, BlobIntegrity.PartHash(payload.Span));
            var result = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await callers.Mcp.CallAsync("keyload_blobs_write_part", part, token))).Value;
            hash = BlobIntegrity.NextHash(hash, part.Ordinal, payload.Length, part.Sha256);
            await Assert.That(result.Value.IntegrityHash).IsEqualTo(hash);
            parts.Add(part);
            written.Add(result);
        }
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, begin.UploadId, hash);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(complete, token));
        var metadata = completed.Value;
        await Assert.That(metadata.Blob).IsEqualTo(blob);
        await Assert.That(metadata.VersionId).IsEqualTo(begin.UploadId);
        await Assert.That(metadata.Revision).IsEqualTo(expectedRevision + 1L);
        await Assert.That(metadata.Length).IsEqualTo(bytes.LongLength);
        await Assert.That(metadata.PartCount).IsEqualTo(parts.Count);
        await Assert.That(metadata.IntegrityHash).IsEqualTo(hash);
        return new(begin, begun, parts.ToArray(), written.ToArray(), complete, completed, bytes);
    }
}
