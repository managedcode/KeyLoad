using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPlacementHysteresisExclusiveRf3Policy
{
    internal static async Task ConfigureAsync(TenantPlacementHysteresisExclusiveRf3State state,
        CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Policy, await McpCallerAssertions.SdkSuccessAsync(
            await state.Seed.Source.ConfigurePlacementPolicyAsync(state.PolicyId, state.Configure, token)));
        await TenantPlacementHysteresisExclusiveRf3Current.RequireAsync(state, token);
        await SqlRf3Protocol.EqualAsync(state.Policy, (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(
            await state.Seed.Official.CallAsync(TenantPlacementPublicProtocol.ConfigureTool,
                new
                {
                    commandId = state.PolicyId,
                    request = state.Configure
                }, token))).Value);
        await TenantPlacementHysteresisExclusiveRf3Current.RequireAsync(state, token);
    }
}
