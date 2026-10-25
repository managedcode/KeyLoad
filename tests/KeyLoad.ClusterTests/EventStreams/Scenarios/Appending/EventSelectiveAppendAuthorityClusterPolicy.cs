using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class EventSelectiveAppendAuthorityClusterPolicy
{
    internal static async Task ConfigureAsync(RequestCqrsClusterCallers administrator,
        EventSelectiveAppendAuthorityClusterState state, bool restore, CancellationToken token)
    {
        var requested = state.Identity.Principal with
        {
            Grants = restore ? state.Identity.Principal.Grants :
                [new(state.Scenario.Partition.DatabaseId, McpEventStreamTokens.StreamSet,
                    Capability.EventsRead)],
            PolicyEpoch = state.CurrentPrincipal.PolicyEpoch + 1
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), requested, token));
        await SqlClusterProtocol.EqualAsync(requested, persisted);
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(restore ? 3L : 2L);
        state.CurrentPrincipal = persisted;
        await EventSelectiveAppendAuthorityClusterCurrent.BaselineAsync(administrator, state, token);
    }
}
