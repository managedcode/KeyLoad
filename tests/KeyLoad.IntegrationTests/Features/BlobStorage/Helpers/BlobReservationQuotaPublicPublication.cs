using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobReservationQuotaPublicPublication
{
    internal sealed record Reservation(BeginBlobUploadRequest First, BlobCommitResult<BlobUploadInfo> Begun,
        BeginBlobUploadRequest Refused);

    internal static async Task<Reservation> ReserveAsync(RequestCqrsRf3Callers callers, CancellationToken token)
    {
        var partition = new PartitionRef("blob-quota-" + Guid.NewGuid().ToString("N"),
            "public-quota", "domain", Guid.NewGuid().ToString("N"));
        var resource = new ResourceDefinition("bounded-blobs", ResourceKind.BlobStore, partition.TransactionDomainId)
        { BlobPolicy = new() { MaxBlobBytes = 4L, MaxReservedBytes = 4L } };
        var configured = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(
            Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, resource), token));
        await SqlRf3Protocol.EqualAsync(resource, configured);
        var first = new BeginBlobUploadRequest(Guid.NewGuid(), new(partition, resource.Name, "reservation"),
            Guid.NewGuid(), 4L, 0L);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(first, token));
        await Assert.That(begun.Value.Blob).IsEqualTo(first.Blob);
        await Assert.That(begun.Value.UploadId).IsEqualTo(first.UploadId);
        await Assert.That(begun.Value.DeclaredLength).IsEqualTo(4L);
        await Assert.That(begun.Value.StoredBytes).IsEqualTo(0L);
        await Assert.That(begun.Value.NextOrdinal).IsEqualTo(0);
        await Assert.That(begun.Value.Status).IsEqualTo(BlobUploadStatus.Active);
        return new(first, begun, new(Guid.NewGuid(), first.Blob with { Id = "healthy-target" }, Guid.NewGuid(), 1L, 0L));
    }

    internal static async Task<BlobSupersessionPublicTrial.Version> PublishAsync(RequestCqrsRf3Callers callers,
        BeginBlobUploadRequest refused, CancellationToken token)
    {
        var begin = refused with { CommandId = Guid.NewGuid() };
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(begin, token));
        byte[] bytes = [71];
        var part = new WriteBlobPartRequest(Guid.NewGuid(), begin.Blob, begin.UploadId, 0, bytes, BlobIntegrity.PartHash(bytes));
        var written = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_write_part", part, token))).Value;
        var hash = BlobIntegrity.NextHash(begun.Value.IntegrityHash, 0, bytes.Length, part.Sha256);
        await Assert.That(written.Value.IntegrityHash).IsEqualTo(hash);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), begin.Blob, begin.UploadId, hash);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteBlobUploadAsync(complete, token));
        await Assert.That(completed.Value.Blob).IsEqualTo(begin.Blob);
        await Assert.That(completed.Value.VersionId).IsEqualTo(begin.UploadId);
        await Assert.That(completed.Value.Revision).IsEqualTo(1L);
        await Assert.That(completed.Value.Length).IsEqualTo(1L);
        await Assert.That(completed.Value.PartCount).IsEqualTo(1);
        await Assert.That(completed.Value.IntegrityHash).IsEqualTo(hash);
        return new(begin, begun, part, written, complete, completed);
    }
}
