using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRejectedContentPublicState
{
    internal static async Task PublishedAsync(RequestCqrsClusterCallers callers,
        BlobSupersessionPublicTrial.Version value, CancellationToken token)
    {
        var actual = value.Completed.Value;
        await Assert.That(actual.Blob).IsEqualTo(value.Begin.Blob);
        await Assert.That(actual.VersionId).IsEqualTo(value.Begin.UploadId);
        await Assert.That(actual.Revision).IsEqualTo(1L);
        await Assert.That(actual.Length).IsEqualTo(4L);
        await Assert.That(actual.PartCount).IsEqualTo(1);
        await Assert.That(actual.IntegrityHash).IsEqualTo(value.Written.Value.IntegrityHash);
        await SqlClusterProtocol.EqualAsync(new RowAccess(), actual.Access);
        foreach (var receipt in new[] { value.Begun.Receipt, value.Written.Receipt, value.Completed.Receipt })
        {
            await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(value.Begin.Blob.Partition.AtomicPartitionId);
            await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        }
        await Assert.That(value.Begun.Receipt.CommandId).IsEqualTo(value.Begin.CommandId);
        await Assert.That(value.Written.Receipt.CommandId).IsEqualTo(value.Part.CommandId);
        await Assert.That(value.Completed.Receipt.CommandId).IsEqualTo(value.Complete.CommandId);
        await CurrentAsync(callers, value, token);
    }

    internal static async Task CurrentAsync(RequestCqrsClusterCallers callers,
        BlobSupersessionPublicTrial.Version value, CancellationToken token)
    {
        var metadata = new BlobMetadataRequest(value.Begin.Blob);
        await SqlClusterProtocol.EqualAsync(value.Completed.Value,
            await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(metadata, token)));
        await SqlClusterProtocol.EqualAsync(value.Completed.Value, (await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_metadata", metadata, token))).Value.Metadata);
        var request = new BlobReadRequest(value.Begin.Blob, 1L, 0L, 4);
        var expected = new BlobReadResult(value.Completed.Value, 0L, new byte[] { 16, 32, 48, 64 });
        await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token))).Value);
    }
}
