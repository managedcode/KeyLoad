using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobTombstoneListingPublicAssertions
{
    internal static async Task CurrentAsync(RequestCqrsClusterCallers callers,
        BlobTombstoneListingPublicState state, CancellationToken token)
    {
        var scope = state.Current.Begin.Blob;
        await PageAsync(callers, scope, 1, null, [], BlobTombstoneListingPublicState.SecondRetiredId, token);
        await PageAsync(callers, scope, 1, BlobTombstoneListingPublicState.SecondRetiredId,
            [state.Current.Completed.Value], BlobTombstoneListingPublicState.CurrentId, token);
        await PageAsync(callers, scope, 1, BlobTombstoneListingPublicState.CurrentId, [], null, token);
        await PageAsync(callers, scope, 4, null, [state.Current.Completed.Value], null, token);
        await BlobBoundedRetirementPublicAssertions.CurrentAsync(callers, state.Current, token);
        await BlobBoundedRetirementPublicAssertions.UploadAsync(callers, scope, state.Current.Begin.UploadId,
            state.Current.Written[0].Value with
            {
                Status = BlobUploadStatus.Complete
            }, token);
        var eof = new BlobReadRequest(scope, 2L, 4L, 0);
        var expected = new BlobReadResult(state.Current.Completed.Value, 4L, Array.Empty<byte>());
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(eof, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_read_range", eof, token))).Value);
        foreach (var old in state.Retired)
        {
            await BlobBoundedRetirementPublicAssertions.HeadAsync(callers, old.Tombstone, token);
            await BlobBoundedRetirementPublicAssertions.UploadAsync(callers, old.Original.Begin.Blob, old.Original.Begin.UploadId, null, token);
        }
        await BlobBoundedRetirementPublicAssertions.UploadAsync(callers, state.Begin.Blob, state.Begin.UploadId, state.Unpublished, token);
        var metadata = new BlobMetadataRequest(state.Begin.Blob);
        await SqlClusterProtocol.EqualAsync<BlobMetadata?>(null, await PublicReadAssertions.MetadataAsync(await callers.Sdk.GetBlobMetadataAsync(metadata, token)));
        await SqlClusterProtocol.EqualAsync(new BlobMetadataReadResult(null), (await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_metadata", metadata, token))).Value);
    }

    private static async Task PageAsync(RequestCqrsClusterCallers callers, BlobRef scope, int limit,
        string? after, BlobMetadata[] items, string? next, CancellationToken token)
    {
        var request = new BlobListRequest(scope.Partition, scope.Resource, limit, after);
        var expected = new BlobListPage([.. items], next);
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ListBlobsAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobListPage>(
            await callers.Mcp.CallAsync(SqlClusterProtocol.BlobListTool, request, token))).Value);
    }

    internal static async Task PublicationReceiptsAsync(BlobBoundedRetirementPublicPublication.Published published)
    {
        await ReceiptAsync(published.Begin.CommandId, published.Begun.Receipt);
        for (var index = 0; index < published.Parts.Length; index++)
        {
            await ReceiptAsync(published.Parts[index].CommandId, published.Written[index].Receipt);
        }
        await ReceiptAsync(published.Complete.CommandId, published.Completed.Receipt);
    }

    internal static async Task ReceiptAsync(Guid commandId, CommitReceipt receipt)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
