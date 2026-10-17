using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventSelectiveAppendAuthorityRf3Policy
{
    internal static async Task ConfigureAsync(RequestCqrsRf3Callers administrator,
        EventSelectiveAppendAuthorityRf3State state, bool restore, CancellationToken token)
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
        await SqlRf3Protocol.EqualAsync(requested, persisted);
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(restore ? 3L : 2L);
        state.CurrentPrincipal = persisted;
        await EventSelectiveAppendAuthorityRf3Current.BaselineAsync(administrator, state, token);
    }
}
