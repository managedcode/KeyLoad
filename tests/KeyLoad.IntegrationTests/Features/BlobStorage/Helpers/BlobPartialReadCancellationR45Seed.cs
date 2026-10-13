using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartialReadCancellationR45Seed
{
    private const string Name = "range-cancel-two-parts";
    internal static async Task<BlobPartialReadCancellationR45State> CreateAsync(
        BlobPublicCancellationOwner owner, BlobMetadata previous, CancellationToken token)
    {
        var blob = new BlobRef(owner.Identity.Partition, BlobPublicCancellationSetup.Resource, Name);
        var request = new AtomicPartitionPlacementReadRequest(1, blob.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(
            await owner.Administrator.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        await SqlRf3Protocol.EqualAsync(placement,
            (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
                await owner.Administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        var publication = await BlobBoundedRetirementPublicPublication.CreateAsync(owner.Caller, blob, 0L,
            BlobPartialReadCancellationR45Corpus.Bytes(0L, BlobPartialReadCancellationR45Corpus.Length), token);
        await BlobPartialReadCancellationR45Publication.RequireAsync(publication, placement);
        var outbox = await BlobLiveListR43Pages.OutboxAsync(owner.Administrator, blob.Partition, token);
        var state = new BlobPartialReadCancellationR45State(publication, placement, outbox, previous);
        await BlobPartialReadCancellationR45Pages.CurrentAsync(owner.Administrator, state, token);
        return state;
    }
}
