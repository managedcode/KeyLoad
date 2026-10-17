using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobReadPolicyPublicPublication
{
    internal static async Task<BlobSupersessionPublicTrial.Version> CreateAsync(
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(administrator.Sdk, token);
        var blob = new BlobRef(scenario.Partition, RelationalSqlRf3Tokens.Blobs, "public-read-policy");
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, 4, 0);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.BeginBlobUploadAsync(begin, token));
        byte[] bytes = [11, 23, 37, 53];
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 0, bytes, BlobIntegrity.PartHash(bytes));
        var written = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.WriteBlobPartAsync(part, token));
        var hash = BlobIntegrity.NextHash(begun.Value.IntegrityHash, 0, bytes.Length, part.Sha256);
        await Assert.That(written.Value.IntegrityHash).IsEqualTo(hash);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var completed = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await administrator.Mcp.CallAsync("keyload_blobs_complete_upload", complete, token))).Value;
        await Assert.That(completed.Value.Blob).IsEqualTo(blob);
        await Assert.That(completed.Value.Revision).IsEqualTo(1L);
        await Assert.That(completed.Value.VersionId).IsEqualTo(upload);
        await Assert.That(completed.Value.Length).IsEqualTo(4L);
        await Assert.That(completed.Value.PartCount).IsEqualTo(1);
        await Assert.That(completed.Value.IntegrityHash).IsEqualTo(hash);
        return new(begin, begun, part, written, complete, completed);
    }
}
