using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3Reads
{
    internal static async Task RunAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        AdaptiveFilteredPublicRf3State state, AnnMaintenanceRequest pin, CancellationToken token)
    {
        foreach (var cohort in AdaptiveFilteredPublicRf3Recipe.Cohorts())
        {
            var request = new ApproximateSearchRequest(AdaptiveFilteredPublicRf3Protocol.Version,
                AdaptiveFilteredPublicRf3Recipe.Search(state, cohort.Allowed, AdaptiveFilteredPublicRf3Protocol.Limit),
                pin.Consumer, pin.IndexGeneration);
            await AdaptiveFilteredPublicRf3Current.ObserveAsync(administrator, state, async () =>
            {
                var exact = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.SearchAsync(request.Search, token));
                await AdaptiveFilteredPublicRf3Equal.RequireAsync(
                    AdaptiveFilteredPublicRf3Recipe.Expected(state.Scenario.Partition, cohort.Eligible,
                        AdaptiveFilteredPublicRf3Protocol.Limit), exact);
            }, token);
            await AdaptiveFilteredPublicRf3Current.ObserveAsync(administrator, state, async () =>
            {
                var exact = (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await reader.Mcp.CallAsync(
                    McpCallerTools.SearchExecute, request.Search, token))).Value;
                await AdaptiveFilteredPublicRf3Equal.RequireAsync(
                    AdaptiveFilteredPublicRf3Recipe.Expected(state.Scenario.Partition, cohort.Eligible,
                        AdaptiveFilteredPublicRf3Protocol.Limit), exact);
            }, token);
            await AdaptiveFilteredPublicRf3Current.ObserveAsync(administrator, state, async () =>
                await AdaptiveFilteredPublicRf3Page.RequireAsync(state, cohort,
                    await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ApproximateSearchAsync(request, token))), token);
            await AdaptiveFilteredPublicRf3Current.ObserveAsync(administrator, state, async () =>
                await AdaptiveFilteredPublicRf3Page.RequireAsync(state, cohort,
                    (await McpCallerAssertions.SuccessAsync<AnnSearchPage>(await reader.Mcp.CallAsync(
                        AdaptiveFilteredPublicRf3Protocol.Tool, request, token))).Value), token);
        }
    }
}
