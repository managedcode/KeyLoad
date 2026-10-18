using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobTombstoneListingPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            callers => SetupAsync(callers, token), token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ColdAsync(fixture, token);
            _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
                async callers => { await BlobTombstoneListingPublicReplay.FinalAsync(callers, state, token); return true; }, token);
        }
        await BlobLiveListR43Trial.RunAsync(fixture, token);
    }

    private static async Task<BlobTombstoneListingPublicState> SetupAsync(RequestCqrsClusterCallers callers, CancellationToken token)
    {
        var scenario = await RelationalSqlClusterScenario.CreateAsync(callers.Sdk, token);
        BlobRef Blob(string id) => new(scenario.Partition, RelationalSqlClusterTokens.Blobs, id);
        var a = await BlobTombstoneListingPublicRetirement.CreateAsync(callers, Blob(BlobTombstoneListingPublicState.FirstRetiredId), token);
        var b = await BlobTombstoneListingPublicRetirement.CreateAsync(callers, Blob(BlobTombstoneListingPublicState.SecondRetiredId), token);
        var first = await BlobBoundedRetirementPublicPublication.CreateAsync(callers, Blob(BlobTombstoneListingPublicState.CurrentId), 0L, [11, 23, 37, 53], token);
        Task First() => BlobBoundedRetirementPublicAssertions.CurrentAsync(callers, first, token);
        await BlobBoundedRetirementPublicReplay.PublicationAsync(callers, first, First, token);
        var current = await BlobBoundedRetirementPublicPublication.CreateAsync(callers, first.Begin.Blob, 1L, [71, 73, 79, 83], token);
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), Blob(BlobTombstoneListingPublicState.UnpublishedId), Guid.NewGuid(), 0L, 0L);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(begin, token));
        await BlobTombstoneListingPublicAssertions.PublicationReceiptsAsync(first);
        await BlobTombstoneListingPublicAssertions.PublicationReceiptsAsync(current);
        var state = new BlobTombstoneListingPublicState([a, b], first, current, begin, begun);
        await BlobTombstoneListingPublicAssertions.ReceiptAsync(begin.CommandId, begun.Receipt);
        Task Observe() => BlobTombstoneListingPublicAssertions.CurrentAsync(callers, state, token);
        await Observe();
        await BlobBoundedRetirementPublicReplay.MatchingAsync(callers, "keyload_blobs_begin_upload", begin, begun,
            ct => callers.Sdk.BeginBlobUploadAsync(begin, ct), Observe, token);
        await BlobTombstoneListingPublicRetirement.RemoveUnpublishedAsync(callers, state, token);
        await BlobTombstoneListingPublicReplay.FinalAsync(callers, state, token);
        return state;
    }

    private static async Task ColdAsync(ClusterFixture fixture, CancellationToken token)
    {
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var failures = new List<Exception>();
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, "blob-tombstone-listing-public", token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginContainerRestartAsync(node, token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
