using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveTailRf3InitialPhase
{
    internal static async Task<(ReadLiveQueryRequest Request, LiveQuerySnapshot Snapshot)> RunAsync(
        RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        FeedLiveRf3Scenario scenario, CancellationToken token)
    {
        var start = reader.Sdk.StartLiveQueryAsync(new(scenario.Query), token);
        var write = scenario.UpdateAsync(administrator, token);
        await Task.WhenAll(start, write);
        var snapshot = await McpCallerAssertions.SdkSuccessAsync(await start);
        var receipt = await write;
        var request = new ReadLiveQueryRequest(scenario.Query, snapshot.Cursor);
        var observeCut = await LiveQueryCutRf3Assertions.CreateAsync(administrator, reader, scenario, receipt,
            [LiveTailRf3Assertions.UpdatedRow], () => LiveTailRf3Assertions.QueryAsync(reader, scenario, token), token);
        var before = await observeCut();
        var tail = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(request, token));
        await LiveQueryCutRf3Assertions.BoundsAsync(before, tail.CutPosition, await observeCut());
        await LiveTailRf3Assertions.SnapshotTailAsync(snapshot, tail, scenario, receipt);
        await LiveTailRf3Assertions.AllPathsAsync(administrator, reader, scenario, request, tail, receipt, token);
        var ordinary = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(scenario.Query, token));
        await SqlRf3Protocol.EqualAsync(new[] { LiveTailRf3Assertions.UpdatedRow }, ordinary.Rows.ToArray());
        await SqlRf3Protocol.EqualAsync(new FeedLiveRf3Scenario.FeedLiveOrder(
            FeedLiveRf3Protocol.UpdatedNumber, FeedLiveRf3Protocol.MatchingStatus),
            JsonSerializer.Deserialize<FeedLiveRf3Scenario.FeedLiveOrder>(ordinary.Rows.Single().Json, JsonDefaults.Options)!);
        var fresh = await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(await reader.Mcp.CallAsync(
            FeedLiveRf3Protocol.LiveStart, new StartLiveQueryRequest(scenario.Query), token));
        await SqlRf3Protocol.EqualAsync(new[] { LiveTailRf3Assertions.UpdatedRow }, fresh.Value.Rows.ToArray());
        return (request, fresh.Value);
    }
}
