using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventCurrentReadAuthorityClusterPolicy
{
    internal static async Task ConfigureAsync(RequestCqrsClusterCallers administrator,
        EventCurrentReadAuthorityClusterState state, bool restore, CancellationToken token)
    {
        var requested = state.Identity.Principal with
        {
            Grants = restore ? state.Identity.Principal.Grants :
                [new(state.Scenario.Partition.DatabaseId, McpEventStreamTokens.StreamSet,
                    Capability.EventsAppend)],
            PolicyEpoch = state.CurrentPrincipal.PolicyEpoch + 1
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), requested, token));
        await SqlClusterProtocol.EqualAsync(requested, persisted);
        state.CurrentPrincipal = persisted;
        await EventCurrentReadAuthorityClusterCurrent.BaselineAsync(administrator, state, token);
    }
}
