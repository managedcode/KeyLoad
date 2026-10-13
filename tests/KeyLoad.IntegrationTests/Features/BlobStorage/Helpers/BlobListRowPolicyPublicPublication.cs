using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobListRowPolicyPublicPublication
{
    internal static readonly string[] Ids = ["a-hidden", "b-visible", "c-hidden", "d-visible"];

    internal static async Task<BlobSupersessionPublicTrial.Version[]> CreateAsync(RequestCqrsRf3Callers admin,
        PartitionRef partition, CancellationToken token)
    {
        var versions = new BlobSupersessionPublicTrial.Version[Ids.Length];
        for (var index = 0; index < Ids.Length; index++)
        { versions[index] = await PublishAsync(admin, partition, index, token); }
        await BlobListRowPolicyPublicAssertions.AllAsync(admin, versions, token);
        return versions;
    }

    private static async Task<BlobSupersessionPublicTrial.Version> PublishAsync(RequestCqrsRf3Callers admin,
        PartitionRef partition, int index, CancellationToken token)
    {
        var blob = new BlobRef(partition, RelationalSqlRf3Tokens.Blobs, Ids[index]);
        var owner = index % 2 == 0 ? BlobListRowPolicyPublicTrial.OwnerB : BlobListRowPolicyPublicTrial.OwnerA;
        byte[] bytes = [checked((byte)(index + 11)), checked((byte)(index + 31))];
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(), bytes.LongLength, 0, new RowAccess(owner));
        var begun = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.BeginBlobUploadAsync(begin, token));
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, begin.UploadId, 0, bytes, BlobIntegrity.PartHash(bytes));
        var written = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.WriteBlobPartAsync(part, token));
        var hash = BlobIntegrity.NextHash(begun.Value.IntegrityHash, 0, bytes.Length, part.Sha256);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, begin.UploadId, hash);
        var completed = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await admin.Mcp.CallAsync("keyload_blobs_complete_upload", complete, token))).Value;
        await Assert.That(completed.Value.Blob).IsEqualTo(blob);
        await Assert.That(completed.Value.Revision).IsEqualTo(1L);
        await Assert.That(completed.Value.Length).IsEqualTo(2L);
        await Assert.That(completed.Value.PartCount).IsEqualTo(1);
        await Assert.That(completed.Value.VersionId).IsEqualTo(begin.UploadId);
        await Assert.That(completed.Value.IntegrityHash).IsEqualTo(hash);
        await SqlRf3Protocol.EqualAsync(begin.Access, completed.Value.Access);
        return new(begin, begun, part, written, complete, completed);
    }
}
