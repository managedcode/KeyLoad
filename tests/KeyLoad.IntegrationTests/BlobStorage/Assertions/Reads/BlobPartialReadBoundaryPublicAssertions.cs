using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartialReadBoundaryPublicAssertions
{
    internal static async Task HealthyAsync(RequestCqrsClusterCallers callers,
        BlobPartialReadBoundaryPublicTrial.Publication publication, CancellationToken token)
    {
        var metadata = publication.Completed.Value;
        await SqlClusterProtocol.EqualAsync(metadata, await PublicReadAssertions.MetadataAsync(
            await callers.Sdk.GetBlobMetadataAsync(new(metadata.Blob), token)));
        await SqlClusterProtocol.EqualAsync(metadata, (await PublicReadAssertions.RequiredMetadataAsync(
            await callers.Mcp.CallAsync("keyload_blobs_metadata", new BlobMetadataRequest(metadata.Blob), token))).Value);
        foreach (var (offset, count) in new (long, int)[]
        {
            (0L, 1), (metadata.Length - 1, 1), (metadata.Length, 0),
            (BlobLimits.RawPartBytes - 3L, BlobPartialReadBoundaryPublicTrial.InteriorLength),
            (BlobLimits.RawPartBytes + 2L, BlobPartialReadBoundaryPublicTrial.InteriorLength)
        })
        {
            var expected = Enumerable.Range(0, count).Select(index =>
                checked((byte)((offset + index) % BlobPartialReadBoundaryPublicTrial.PatternModulo))).ToArray();
            var request = new BlobReadRequest(metadata.Blob, 1, offset, count);
            var result = new BlobReadResult(metadata, offset, expected);
            await SqlClusterProtocol.EqualAsync(result,
                await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(request, token)));
            await SqlClusterProtocol.EqualAsync(result, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
                await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token))).Value);
        }
    }

    internal static async Task RefusalsAsync(RequestCqrsClusterCallers callers,
        BlobPartialReadBoundaryPublicTrial.Publication publication, CancellationToken token)
    {
        var metadata = publication.Completed.Value;
        foreach (var request in new[]
        {
            new BlobReadRequest(metadata.Blob, 1, metadata.Length, 1),
            new BlobReadRequest(metadata.Blob, 1, metadata.Length + 1, 0),
            new BlobReadRequest(metadata.Blob, 1, long.MaxValue, 1)
        })
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var refused = await callers.Sdk.ReadBlobRangeAsync(request, token);
                await Assert.That(refused.IsFailed).IsTrue();
                await Assert.That(refused.Value).IsNull();
                await Assert.That(refused.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
                await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync("keyload_blobs_read_range", request, token),
                    ErrorCode.Validation, dispatched: true);
            }
            await HealthyAsync(callers, publication, token);
        }
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers callers,
        BlobPartialReadBoundaryPublicTrial.Publication publication, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(publication.Begun, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.BeginBlobUploadAsync(publication.Begin, token)));
        await SqlClusterProtocol.EqualAsync(publication.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await callers.Mcp.CallAsync("keyload_blobs_begin_upload", publication.Begin, token))).Value);
        for (var index = 0; index < publication.Parts.Length; index++)
        {
            await SqlClusterProtocol.EqualAsync(publication.Written[index], await McpCallerAssertions.SdkSuccessAsync(
                await callers.Sdk.WriteBlobPartAsync(publication.Parts[index], token)));
            await SqlClusterProtocol.EqualAsync(publication.Written[index], (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await callers.Mcp.CallAsync("keyload_blobs_write_part", publication.Parts[index], token))).Value);
        }
        await SqlClusterProtocol.EqualAsync(publication.Completed, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.CompleteBlobUploadAsync(publication.Complete, token)));
        await SqlClusterProtocol.EqualAsync(publication.Completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await callers.Mcp.CallAsync("keyload_blobs_complete_upload", publication.Complete, token))).Value);
        await Assert.That(publication.Begun.Receipt.CommandId).IsEqualTo(publication.Begin.CommandId);
        for (var index = 0; index < publication.Parts.Length; index++)
        {
            await Assert.That(publication.Written[index].Receipt.CommandId).IsEqualTo(publication.Parts[index].CommandId);
        }
        await Assert.That(publication.Completed.Receipt.CommandId).IsEqualTo(publication.Complete.CommandId);
        await Assert.That(publication.Completed.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(publication.Completed.Receipt.Token.AtomicPartitionId).IsEqualTo(publication.Begin.Blob.Partition.AtomicPartitionId);
    }
}
