using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateBlobs
{
    private const int Resources = 3;

    internal static async Task InitializeAsync(FunctionalGateContext context, CancellationToken token)
    {
        for (var index = 0; index < Resources; index++)
        {
            var name = $"gate_blobs_{index}";
            var definition = new ResourceDefinition(name, ResourceKind.BlobStore, context.Partition.TransactionDomainId);
            var configure = new ConfigureResourceRequest(context.Partition.TenantId, context.Partition.DatabaseId, definition);
            await SqlRf3Protocol.EqualAsync(definition,
                await context.SdkAsync(context.Administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(), configure, token)));
            var baseline = await FunctionalGateBlobUpload.PublishAsync(context,
                new BlobRef(context.Partition, name, "baseline"), -1 - index, token);
            await FunctionalGateBlobAssertions.ContentAsync(context, baseline.Published.Value, baseline.Bytes, token);
            await FunctionalGateBlobLinks.LinkAsync(context, baseline, -1 - index, token);
        }
    }

    internal static async Task ExerciseAsync(FunctionalGateContext context, int iteration, int resourceIndex, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(resourceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(resourceIndex, Resources);
        var index = resourceIndex;
        var blob = new BlobRef(context.Partition, $"gate_blobs_{index}", $"iteration_{iteration}");
        var published = await FunctionalGateBlobUpload.PublishAsync(context, blob, iteration, token);
        await FunctionalGateBlobAssertions.ContentAsync(context, published.Published.Value, published.Bytes, token);
        var link = await FunctionalGateBlobLinks.LinkAsync(context, published, iteration, token);
        await FunctionalGateBlobAssertions.DeniedAsync(context, published, token);
        await FunctionalGateBlobLinks.UnlinkAsync(context, link, token);
        await FunctionalGateBlobAssertions.ContentAsync(context, published.Published.Value, published.Bytes, token);
        await RetireAsync(context, published, token);
        var baseline = blob with
        {
            Id = "baseline"
        };
        await FunctionalGateBlobAssertions.BaselineAsync(context, baseline, index, token);
        await FunctionalGateBlobLinks.BaselineAsync(context, baseline, index, token);
    }

    private static async Task RetireAsync(FunctionalGateContext context,
        FunctionalGateBlobVersion published, CancellationToken token)
    {
        var delete = new DeleteBlobRequest(Guid.NewGuid(), published.Begin.Blob, 1L);
        var deleted = await context.SdkAsync(context.Writer.Sdk.DeleteBlobAsync(delete, token));
        await FunctionalGateBlobAssertions.ReceiptAsync(deleted.Receipt, delete.CommandId, delete.Blob);
        var expected = published.Published.Value with
        {
            Revision = 2L,
            VersionId = null,
            Length = 0L,
            PartCount = 0,
            IntegrityHash = null,
            Deleted = true,
            UpdatedAt = deleted.Value.UpdatedAt
        };
        await SqlRf3Protocol.EqualAsync(expected, deleted.Value);
        await SqlRf3Protocol.EqualAsync(deleted, await context.McpAsync<BlobCommitResult<BlobMetadata>>(
            context.Writer, "keyload_blobs_delete", delete, token));
        await FunctionalGateBlobAssertions.RetiredAsync(context, published, expected, token);
        var reclaim = new ReclaimBlobRequest(Guid.NewGuid(), delete.Blob, published.Begin.UploadId, 2);
        var reclaimed = await context.McpAsync<BlobCommitResult<BlobReclaimResult>>(
            context.Writer, "keyload_blobs_reclaim", reclaim, token);
        await FunctionalGateBlobAssertions.ReceiptAsync(reclaimed.Receipt, reclaim.CommandId, delete.Blob);
        await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(published.Begin.UploadId, 2, 0, published.Bytes.Length, true), reclaimed.Value);
        await SqlRf3Protocol.EqualAsync(reclaimed,
            await context.SdkAsync(context.Writer.Sdk.ReclaimBlobAsync(reclaim, token)));
        var info = new BlobUploadInfoRequest(delete.Blob, published.Begin.UploadId);
        await SqlRf3Protocol.EqualAsync<BlobUploadInfo?>(null,
            (await context.SdkAsync(context.Writer.Sdk.GetBlobUploadInfoAsync(info, token))).Upload);
        await SqlRf3Protocol.EqualAsync<BlobUploadInfo?>(null, (await context.McpAsync<BlobUploadInfoReadResult>(
            context.Writer, "keyload_blobs_upload_info", info, token)).Upload);
        await FunctionalGateBlobAssertions.RetiredAsync(context, published, expected, token);
    }
}
