using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class FeedLiveRf3NoEffects
{
    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator,
        FeedLiveRf3Scenario scenario, Func<Task> refused, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.QueryAstAsync(scenario.Query, token));
        var head = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await refused();
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.QueryAstAsync(scenario.Query, token));
        var retained = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await SqlRf3Protocol.EqualAsync(before, after);
        await SqlRf3Protocol.EqualAsync(head, retained);
    }
}
