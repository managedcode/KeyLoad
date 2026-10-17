using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRowCreatorPublicReplay
{
    internal static async Task RunAsync(RequestCqrsRf3Callers creator,
        BlobSupersessionPublicTrial.Version publication, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(publication.Begun, await McpCallerAssertions.SdkSuccessAsync(
            await creator.Sdk.BeginBlobUploadAsync(publication.Begin, token)));
        await BlobRowCreatorPublicAssertions.CurrentAsync(creator, publication, token);
        await SqlRf3Protocol.EqualAsync(publication.Begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await creator.Mcp.CallAsync("keyload_blobs_begin_upload", publication.Begin, token))).Value);
        await BlobRowCreatorPublicAssertions.CurrentAsync(creator, publication, token);
        await SqlRf3Protocol.EqualAsync(publication.Written, await McpCallerAssertions.SdkSuccessAsync(
            await creator.Sdk.WriteBlobPartAsync(publication.Part, token)));
        await BlobRowCreatorPublicAssertions.CurrentAsync(creator, publication, token);
        await SqlRf3Protocol.EqualAsync(publication.Written, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await creator.Mcp.CallAsync("keyload_blobs_write_part", publication.Part, token))).Value);
        await BlobRowCreatorPublicAssertions.CurrentAsync(creator, publication, token);
        await SqlRf3Protocol.EqualAsync(publication.Completed, await McpCallerAssertions.SdkSuccessAsync(
            await creator.Sdk.CompleteBlobUploadAsync(publication.Complete, token)));
        await BlobRowCreatorPublicAssertions.CurrentAsync(creator, publication, token);
        await SqlRf3Protocol.EqualAsync(publication.Completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await creator.Mcp.CallAsync("keyload_blobs_complete_upload", publication.Complete, token))).Value);
        await BlobRowCreatorPublicAssertions.CurrentAsync(creator, publication, token);
        await Assert.That(publication.Begun.Receipt.CommandId).IsEqualTo(publication.Begin.CommandId);
        await Assert.That(publication.Written.Receipt.CommandId).IsEqualTo(publication.Part.CommandId);
        await Assert.That(publication.Completed.Receipt.CommandId).IsEqualTo(publication.Complete.CommandId);
        await Assert.That(publication.Completed.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(publication.Completed.Receipt.Token.AtomicPartitionId).IsEqualTo(publication.Begin.Blob.Partition.AtomicPartitionId);
    }
}
