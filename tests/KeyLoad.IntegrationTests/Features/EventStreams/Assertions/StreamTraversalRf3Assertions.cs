using KeyLoad.IntegrationTests.Features.ChangeFeeds;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class StreamTraversalRf3Assertions
{
    internal static async Task RoutesAsync(RequestCqrsRf3Callers callers, McpEventStreamScenario scenario,
        ReadStreamRequest request, long[] revisions, EventData[] expected, long tail,
        long snapshotCut, long minimumCut, CancellationToken token)
    {
        await PageAsync(scenario, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReadStreamAsync(request, token)), revisions, expected, tail, snapshotCut, minimumCut);
        await PageAsync(scenario, (await McpCallerAssertions.SuccessAsync<StreamPage>(
            await callers.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token))).Value,
            revisions, expected, tail, snapshotCut, minimumCut);
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request);
        await PageAsync(scenario, await SqlRf3Protocol.SdkAsync<StreamPage>(callers.Sdk, sql, token),
            revisions, expected, tail, snapshotCut, minimumCut);
        await PageAsync(scenario, await SqlRf3Protocol.McpAsync<StreamPage>(callers.Mcp, sql, token),
            revisions, expected, tail, snapshotCut, minimumCut);
    }

    internal static async Task PageAsync(McpEventStreamScenario scenario, StreamPage actual,
        long[] revisions, EventData[] expected, long tail, long snapshotCut, long minimumCut,
        bool more = false, string? cursor = null)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(minimumCut);
        await Assert.That(actual.SnapshotCutPosition).IsEqualTo(snapshotCut);
        await Assert.That(actual.SnapshotCutPosition).IsLessThanOrEqualTo(actual.CutPosition);
        await Assert.That(actual.Events.Length).IsEqualTo(expected.Length);
        var records = new EventRecord[expected.Length];
        for (var index = StreamTraversalTestProtocol.Empty; index < records.Length; index++)
        {
            await Assert.That(actual.Events[index].RecordedAt).IsNotEqualTo(default(DateTimeOffset));
            records[index] = new(scenario.Stream, revisions[index], revisions[index], expected[index], actual.Events[index].RecordedAt);
        }
        await SqlRf3Protocol.EqualAsync(new StreamPage(scenario.Stream, new(tail, StreamTraversalTestProtocol.First, StreamTraversalTestProtocol.First),
            [.. records], actual.CutPosition, more, cursor, snapshotCut), actual);
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers callers, McpEventStreamScenario scenario,
        ReadStreamRequest request, ErrorCode code, CancellationToken token)
    {
        var sdk = await callers.Sdk.ReadStreamAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(code.ToString());
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.StreamsRead,
            request, token), code, dispatched: true);
        await LivePageRf3Assertions.DeniedSqlAsync(callers,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request), code, token);
    }
}
