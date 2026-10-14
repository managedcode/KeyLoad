using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobMaintenancePolicyPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            administrator => SetupAsync(fixture, administrator, token), token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ColdAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
                async creator => { await BlobMaintenancePolicyPublicVerification.FinalAsync(creator, state, token); return true; }, token);
        }
    }

    private static async Task<BlobMaintenancePolicyPublicState> SetupAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(administrator.Sdk, token);
        var blob = new BlobRef(scenario.Partition, RelationalSqlRf3Tokens.Blobs, "maintenance-policy");
        var identity = await McpPersistedIdentity.CreateAsync(fixture, blob.Partition, blob.Resource,
            Capability.BlobRead | Capability.BlobWrite | Capability.BlobDelete | Capability.BlobManage, token);
        await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
        var request = new AtomicPartitionPlacementReadRequest(1, blob.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value;
        await SqlRf3Protocol.EqualAsync(placement, official);
        return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret, async creator =>
        {
            var original = await BlobBoundedRetirementPublicPublication.CreateAsync(creator, blob, 0L, [11, 23, 37, 53], token);
            var state = new BlobMaintenancePolicyPublicState(blob, identity, original, placement);
            Task Published() => BlobMaintenancePolicyPublicAssertions.PublishedAsync(creator, original, state.Placement, token);
            await BlobBoundedRetirementPublicReplay.PublicationAsync(creator, original, Published, token);
            await ExerciseAsync(administrator, creator, state, token);
            return state;
        }, token);
    }

    private static async Task ExerciseAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers creator,
        BlobMaintenancePolicyPublicState state, CancellationToken token)
    {
        Task Published() => BlobMaintenancePolicyPublicAssertions.PublishedAsync(creator, state.Original, state.Placement, token);
        await BlobMaintenancePolicyPublicOperations.PersistAsync(administrator, state.Identity.Principal, Capability.BlobDelete, 2L, token);
        await BlobMaintenancePolicyPublicAssertions.DeleteDeniedAsync(creator, state.DeniedDelete, Published, token);
        await BlobMaintenancePolicyPublicOperations.PersistAsync(administrator, state.Identity.Principal, Capability.None, 3L, token);
        await BlobMaintenancePolicyPublicAssertions.DeleteDeniedAsync(creator, state.DeniedDelete, Published, token);
        await BlobMaintenancePolicyPublicOperations.DeleteAsync(creator, state, token);
        Task Retired() => BlobMaintenancePolicyPublicAssertions.RetiredAsync(creator, state, token);
        await BlobMaintenancePolicyPublicOperations.PersistAsync(administrator, state.Identity.Principal, Capability.BlobManage, 4L, token);
        await BlobMaintenancePolicyPublicAssertions.ReclaimDeniedAsync(creator, state, Retired, token);
        await BlobMaintenancePolicyPublicOperations.PersistAsync(administrator, state.Identity.Principal, Capability.None, 5L, token);
        await BlobMaintenancePolicyPublicAssertions.ReclaimDeniedAsync(creator, state, Retired, token);
        await BlobMaintenancePolicyPublicOperations.ReclaimAndPublishAsync(creator, state, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, CancellationToken token)
    {
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var failures = new List<Exception>();
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, "blob-maintenance-policy-public", token), failures);
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
