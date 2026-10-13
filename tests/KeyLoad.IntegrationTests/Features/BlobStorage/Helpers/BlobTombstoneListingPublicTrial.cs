using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobTombstoneListingPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            callers => SetupAsync(callers, token), token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ColdAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
                async callers => { await BlobTombstoneListingPublicReplay.FinalAsync(callers, state, token); return true; }, token);
        }
    }

    private static async Task<BlobTombstoneListingPublicState> SetupAsync(RequestCqrsRf3Callers callers, CancellationToken token)
    {
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(callers.Sdk, token);
        var request = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await callers.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        BlobRef Blob(string id) => new(scenario.Partition, RelationalSqlRf3Tokens.Blobs, id);
        var a = await BlobTombstoneListingPublicRetirement.CreateAsync(callers, Blob(BlobTombstoneListingPublicState.FirstRetiredId), placement, token);
        var b = await BlobTombstoneListingPublicRetirement.CreateAsync(callers, Blob(BlobTombstoneListingPublicState.SecondRetiredId), placement, token);
        var first = await BlobBoundedRetirementPublicPublication.CreateAsync(callers, Blob(BlobTombstoneListingPublicState.CurrentId), 0L, [11, 23, 37, 53], token);
        Task First() => BlobBoundedRetirementPublicAssertions.CurrentAsync(callers, first, token);
        await BlobBoundedRetirementPublicReplay.PublicationAsync(callers, first, First, token);
        var current = await BlobBoundedRetirementPublicPublication.CreateAsync(callers, first.Begin.Blob, 1L, [71, 73, 79, 83], token);
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), Blob(BlobTombstoneListingPublicState.UnpublishedId), Guid.NewGuid(), 0L, 0L);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(begin, token));
        await BlobTombstoneListingPublicAssertions.PublicationReceiptsAsync(placement, first);
        await BlobTombstoneListingPublicAssertions.PublicationReceiptsAsync(placement, current);
        var state = new BlobTombstoneListingPublicState(placement, [a, b], first, current, begin, begun);
        await BlobTombstoneListingPublicAssertions.ReceiptAsync(placement, begin.CommandId, begun.Receipt);
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
        { await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, "blob-tombstone-listing-public", token), failures); }
        foreach (var node in nodes)
        { await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginContainerRestartAsync(node, token), failures); }
        foreach (var node in nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
