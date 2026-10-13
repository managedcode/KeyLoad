using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobActivePolicyPublicTrial
{
    private const string BlobId = "active-policy-public";
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            administrator => SetupAsync(fixture, administrator, token), token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ColdAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                administrator => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                    state.Identity.Secret, async creator =>
                    {
                        await BlobActivePolicyPublicAssertions.CurrentAsync(administrator, creator, state, token);
                        await BlobActivePolicyPublicAssertions.DeniedAsync(administrator, creator, state, true, token);
                        await BlobActivePolicyPublicOperations.ReplayAsync(administrator, creator, state, token);
                        return true;
                    }, token), token);
        }
    }

    private static async Task<BlobActivePolicyPublicState> SetupAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(administrator.Sdk, token);
        var blob = new BlobRef(scenario.Partition, RelationalSqlRf3Tokens.Blobs, BlobId);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, blob.Partition, blob.Resource,
            Capability.BlobRead | Capability.BlobWrite, token);
        var request = new AtomicPartitionPlacementReadRequest(1, blob.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        var state = new BlobActivePolicyPublicState(blob, identity, placement);
        return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret,
            async creator =>
            {
                await BlobActivePolicyPublicOperations.BeginAsync(creator, state, token);
                await BlobActivePolicyPublicAssertions.CurrentAsync(administrator, creator, state, token);
                await BlobBoundedRetirementPublicReplay.MatchingAsync(creator, "keyload_blobs_begin_upload", state.Begin,
                    state.Begun, ct => creator.Sdk.BeginBlobUploadAsync(state.Begin, ct),
                    () => BlobActivePolicyPublicAssertions.CurrentAsync(administrator, creator, state, token), token);
                await PersistAsync(administrator, identity.Principal, true, 2L, token);
                await BlobActivePolicyPublicAssertions.DeniedAsync(administrator, creator, state, true, token);
                await UploadDeniedAsync(administrator, creator, state, token);
                await PersistAsync(administrator, identity.Principal, false, 3L, token);
                await BlobActivePolicyPublicAssertions.DeniedAsync(administrator, creator, state, true, token);
                await BlobActivePolicyPublicOperations.PublishAsync(administrator, creator, state, token);
                await BlobActivePolicyPublicAssertions.DeniedAsync(administrator, creator, state, true, token);
                await BlobActivePolicyPublicOperations.ReplayAsync(administrator, creator, state, token);
                return state;
            }, token);
    }

    private static async Task PersistAsync(RequestCqrsRf3Callers administrator, PrincipalRecord original,
        bool revoke, long epoch, CancellationToken token)
    {
        var expected = original with
        {
            PolicyEpoch = epoch,
            Grants = [.. original.Grants.Select(grant => grant with
            { Capabilities = revoke ? grant.Capabilities & ~Capability.BlobWrite : grant.Capabilities })]
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }

    private static async Task UploadDeniedAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers creator, BlobActivePolicyPublicState state, CancellationToken token)
    {
        var request = new BlobUploadInfoRequest(state.Blob, state.Begin.UploadId);
        Task Observe() => BlobActivePolicyPublicAssertions.CurrentAsync(administrator, creator, state, token);
        await BlobActivePolicyPublicAssertions.RefusedAsync(creator, "keyload_blobs_upload_info", request,
            ct => creator.Sdk.GetBlobUploadInfoAsync(request, ct), Observe, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, CancellationToken token)
    {
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var failures = new List<Exception>();
        foreach (var node in nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, "blob-active-policy-public", token), failures); }
        foreach (var node in nodes)
        { await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginContainerRestartAsync(node, token), failures); }
        foreach (var node in nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
