using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRowCreatorPublicPublication
{
    internal static readonly byte[] Bytes = [17, 29, 43, 61];

    internal static async Task<BlobSupersessionPublicTrial.Version> RunAsync(RequestCqrsClusterCallers creator,
        RequestCqrsClusterCallers other, McpPersistedIdentity creatorIdentity, McpPersistedIdentity otherIdentity,
        BlobRef blob, CancellationToken token)
    {
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(), Bytes.LongLength, 0,
            new RowAccess(creatorIdentity.Principal.OwnerId));
        var begun = await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.BeginBlobUploadAsync(begin, token));
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, begin.UploadId, 0, Bytes, BlobIntegrity.PartHash(Bytes));
        Func<Task> active = () => BlobRowCreatorPublicAssertions.ActiveAsync(creator, begin, begun.Value, token);
        await active();
        await BlobRowCreatorPublicAssertions.RefusedAsync(
            () => other.Sdk.GetBlobUploadInfoAsync(new(blob, begin.UploadId), token),
            () => other.Mcp.CallAsync("keyload_blobs_upload_info", new BlobUploadInfoRequest(blob, begin.UploadId), token),
            otherIdentity.Secret, creatorIdentity.Secret, active);
        await BlobRowCreatorPublicAssertions.RefusedAsync(() => other.Sdk.WriteBlobPartAsync(part, token),
            () => other.Mcp.CallAsync("keyload_blobs_write_part", part, token), otherIdentity.Secret, creatorIdentity.Secret, active);
        var written = await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.WriteBlobPartAsync(part, token));
        var hash = BlobIntegrity.NextHash(begun.Value.IntegrityHash, 0, Bytes.Length, part.Sha256);
        await Assert.That(written.Value.StoredBytes).IsEqualTo(Bytes.LongLength);
        await Assert.That(written.Value.NextOrdinal).IsEqualTo(1);
        await Assert.That(written.Value.IntegrityHash).IsEqualTo(hash);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, begin.UploadId, hash);
        await BlobRowCreatorPublicAssertions.RefusedAsync(() => other.Sdk.CompleteBlobUploadAsync(complete, token),
            () => other.Mcp.CallAsync("keyload_blobs_complete_upload", complete, token), otherIdentity.Secret, creatorIdentity.Secret,
            () => BlobRowCreatorPublicAssertions.ActiveAsync(creator, begin, written.Value, token));
        var completed = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await creator.Mcp.CallAsync("keyload_blobs_complete_upload", complete, token))).Value;
        await Assert.That(completed.Value.Blob).IsEqualTo(blob);
        await Assert.That(completed.Value.Revision).IsEqualTo(1L);
        await Assert.That(completed.Value.Length).IsEqualTo(Bytes.LongLength);
        await Assert.That(completed.Value.VersionId).IsEqualTo(begin.UploadId);
        await Assert.That(completed.Value.PartCount).IsEqualTo(1);
        await Assert.That(completed.Value.IntegrityHash).IsEqualTo(hash);
        await SqlClusterProtocol.EqualAsync(begin.Access, completed.Value.Access);
        var published = new BlobSupersessionPublicTrial.Version(begin, begun, part, written, complete, completed);
        await BlobRowCreatorPublicAssertions.CurrentAsync(creator, published, token);
        await BlobRowCreatorPublicReplay.RunAsync(creator, published, token);
        return published;
    }
}
