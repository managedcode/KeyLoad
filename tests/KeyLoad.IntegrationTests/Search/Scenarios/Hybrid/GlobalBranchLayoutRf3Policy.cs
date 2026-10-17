using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GlobalBranchLayoutRf3Policy
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, DistributedSearchRf3Callers callers,
        GlobalBranchLayoutRf3State state, long deniedEpoch, long repairedEpoch, CancellationToken token)
    {
        var source = callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
        var target = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
        var denied = state.Seed.Original.Destination.Principal with
        {
            PolicyEpoch = deniedEpoch,
            Grants = [new(RemoteDocumentRf3Protocol.Database, RemoteDocumentRf3Protocol.Collection,
                Capability.Query | Capability.DocumentsRead)]
        };
        await SqlRf3Protocol.EqualAsync(denied, await McpCallerAssertions.SdkSuccessAsync(
            await target.ConfigurePrincipalAsync(Guid.NewGuid(), denied, token)));
        await GlobalBranchLayoutRf3Calls.DeniedAsync(wave, callers, state, token);
        await DistributedSearchRf3Seed.ConfigureAsync(target, state.Seed.Original.Destination.Principal, repairedEpoch, token);
        await DistributedSearchRf3Seed.ConfigureAsync(source, state.Seed.Original.Destination.Principal, repairedEpoch, token);
        await GlobalBranchLayoutRf3Calls.HealthyAsync(wave, callers, state, repairedEpoch, null, token);
    }
}
