using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3Policy
{
    internal static async Task RunAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        AdaptiveFilteredPublicRf3State state, AnnMaintenanceRequest pin, CancellationToken token)
    {
        var principal = state.Reader.Principal;
        for (var phase = 0; phase < 2; phase++)
        {
            var denied = principal with
            {
                PolicyEpoch = checked(principal.PolicyEpoch + 1),
                Grants = [new(state.Scenario.Partition.DatabaseId, NativeAnnMaintenanceRf3Scenario.Collection,
                    phase == 0 ? Capability.Query | Capability.DocumentsRead : AdaptiveFilteredPublicRf3Protocol.Healthy)],
                FieldGrants = phase == 0 ? [AdaptiveFilteredPublicRf3Protocol.FieldGrant] : []
            };
            await PersistAsync(administrator, denied, token);
            var request = new ApproximateSearchRequest(AdaptiveFilteredPublicRf3Protocol.Version,
                AdaptiveFilteredPublicRf3Recipe.Search(state, [], AdaptiveFilteredPublicRf3Protocol.Limit), pin.Consumer, pin.IndexGeneration);
            await AdaptiveFilteredPublicRf3Refusals.DeniedAsync(administrator, reader, state, request,
                phase == 0 ? AdaptiveFilteredPublicRf3Protocol.ScopeDenied : AdaptiveFilteredPublicRf3Protocol.FieldDenied,
                phase == 0 ? AdaptiveFilteredPublicRf3Protocol.ScopeDenied : AdaptiveFilteredPublicRf3Protocol.SafeDenied, token);
            principal = state.Reader.Principal with
            {
                PolicyEpoch = checked(denied.PolicyEpoch + 1)
            };
            await PersistAsync(administrator, principal, token);
            await AdaptiveFilteredPublicRf3Reads.RunAsync(administrator, reader, state, pin, token);
        }
    }
    private static async Task PersistAsync(RequestCqrsRf3Callers administrator, PrincipalRecord principal, CancellationToken token)
        => await AdaptiveFilteredPublicRf3Equal.RequireAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
}
