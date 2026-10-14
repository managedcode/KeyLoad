using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class DiscoveryAuthorityFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, DiscoveryAuthorityState state, CancellationToken token)
    {
        await DiscoveryAuthorityClients.RunAsync(fixture, state, changePolicy: true, token);
        await DiscoveryAuthorityCold.RunAsync(fixture, state, token);
    }

    internal static async Task WarmAsync(ClusterFixture fixture, KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, DiscoveryAuthorityState state, Guid firstRequest, CancellationToken token)
    {
        var restricted = state.Identity.Principal with
        {
            Grants = [],
            PolicyEpoch = state.Identity.Principal.PolicyEpoch + McpCallerProtocol.EpochIncrement
        };
        _ = await PersistAsync(root, Guid.NewGuid(), restricted, token);
        await DiscoveryAuthorityRefusals.RestrictedAsync(root, reader, session, state, firstRequest, token);
        _ = await PersistAsync(root, Guid.NewGuid(), state.Identity.Principal with
        {
            PolicyEpoch = DiscoveryAuthorityProtocol.RepairEpoch
        }, token);
        await DiscoveryAuthorityValues.HealthyAsync(root, reader, session, state, token);
        _ = await PersistAsync(root, Guid.NewGuid(), state.Identity.Principal with
        {
            Revoked = true,
            PolicyEpoch = DiscoveryAuthorityProtocol.RevokedEpoch
        }, token);
        await DiscoveryAuthorityRefusals.RevokedAsync(fixture, root, reader, session, state, token);
        state.OriginalRepairResult = await PersistAsync(root, state.RepairCommandId, state.Repaired, token);
        await DiscoveryAuthorityValues.HealthyAsync(root, reader, session, state, token);
        await DiscoveryAuthorityValues.ReplayAsync(root, state, token);
    }

    private static async Task<PrincipalRecord> PersistAsync(KeyLoadClient root, Guid commandId, PrincipalRecord expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(commandId, expected, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
        return actual;
    }
}
