using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class LiveTailClusterInitialPhase
{
    internal static async Task<(ReadLiveQueryRequest Request, LiveQuerySnapshot Snapshot)> RunAsync(
        RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        FeedLiveClusterScenario scenario, CancellationToken token)
    {
        var start = reader.Sdk.StartLiveQueryAsync(new(scenario.Query), token);
        var write = scenario.UpdateAsync(administrator, token);
        await Task.WhenAll(start, write);
        var snapshot = await McpCallerAssertions.SdkSuccessAsync(await start);
        var receipt = await write;
        var request = new ReadLiveQueryRequest(scenario.Query, snapshot.Cursor);
        var observeCut = await LiveQueryCutClusterAssertions.CreateAsync(administrator, reader, scenario, receipt,
            [LiveTailClusterAssertions.UpdatedRow], () => LiveTailClusterAssertions.QueryAsync(reader, scenario, token), token);
        var before = await observeCut();
        var tail = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(request, token));
        await LiveQueryCutClusterAssertions.BoundsAsync(before, tail.CutPosition, await observeCut());
        await LiveTailClusterAssertions.SnapshotTailAsync(snapshot, tail, scenario, receipt);
        await LiveTailClusterAssertions.AllPathsAsync(administrator, reader, scenario, request, tail, receipt, token);
        var ordinary = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(scenario.Query, token));
        await SqlClusterProtocol.EqualAsync(new[] { LiveTailClusterAssertions.UpdatedRow }, ordinary.Rows.ToArray());
        await SqlClusterProtocol.EqualAsync(new FeedLiveClusterScenario.FeedLiveOrder(
            FeedLiveClusterProtocol.UpdatedNumber, FeedLiveClusterProtocol.MatchingStatus),
            JsonSerializer.Deserialize<FeedLiveClusterScenario.FeedLiveOrder>(ordinary.Rows.Single().Json, JsonDefaults.Options)!);
        var fresh = await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(await reader.Mcp.CallAsync(
            FeedLiveClusterProtocol.LiveStart, new StartLiveQueryRequest(scenario.Query), token));
        await SqlClusterProtocol.EqualAsync(new[] { LiveTailClusterAssertions.UpdatedRow }, fresh.Value.Rows.ToArray());
        return (request, fresh.Value);
    }
}
