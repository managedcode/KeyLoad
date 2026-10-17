using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AnnLifetimeRf3Flow
{
    private const int ColdOwners = 2;
    private const long NextEpoch = 1;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        var administrator = await PartitionMovementPublicParentRf3Administrator.PersistAsync(wave, token);
        RemoteAnnPublicationRf3State state;
        McpPersistedIdentity reader;
        await using (var admin = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, administrator, token))
        {
            state = await RemoteAnnPublicationRf3Build.SeedAsync(admin.Sdk, admin.Mcp, token);
            reader = await AnnPublicCancellationAssertions.IdentityAsync(admin.Sdk, state.Scenario.Partition, token);
        }
        var principal = reader.Principal;
        await AnnLifetimeRf3Round.DeniedAsync(wave, reader.Secret, state, token);
        principal = principal with
        {
            PolicyEpoch = checked(principal.PolicyEpoch + NextEpoch),
            FieldGrants = [RemoteAnnPublicationRf3Protocol.RawRead, RemoteAnnPublicationRf3Protocol.RawUse]
        };
        await AnnLifetimeRf3Round.ConfigureAsync(wave, administrator, principal, token);
        foreach (var wait in new[] { false, true })
        {
            foreach (var mcp in new[] { false, true })
            {
                principal = await AnnLifetimeRf3Round.RunAsync(wave, administrator, reader.Secret, state,
                    principal, wait, mcp, token);
            }
        }
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            await wave.RestartJoinedAsync(token);
            await RemoteAnnPublicationRf3Round.RestoreAsync(wave, administrator, TwoRf3MembershipProtocol.Node1, state, token);
            await AnnLifetimeRf3Round.HealthyAsync(wave, administrator, reader.Secret, state, principal.PolicyEpoch, token);
        }
    }
}
