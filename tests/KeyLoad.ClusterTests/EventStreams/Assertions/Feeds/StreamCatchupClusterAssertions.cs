using KeyLoad.ClusterTests.ChangeFeeds;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class StreamCatchupClusterAssertions
{
    internal static async Task PageAsync(McpEventStreamScenario scenario, StreamPage actual,
        long tail, EventData[] expected, long minimum)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(minimum);
        await Assert.That(actual.Events.Length).IsEqualTo(expected.Length);
        await Assert.That(actual.SnapshotCutPosition).IsEqualTo(actual.CutPosition);
        await Assert.That(actual.Cursor).IsNull();
        var records = new EventRecord[expected.Length];
        for (var index = 0; index < records.Length; index++)
        {
            var data = expected[index];
            var revision = data.EventId == StreamCatchupColdClusterFlow.TailEvent.EventId
                ? StreamCatchupColdClusterFlow.TailRevision
                : McpEventStreamScenario.InputEvents.IndexOf(data) + McpEventStreamTokens.FirstEventRevision;
            await Assert.That(actual.Events[index].RecordedAt).IsNotEqualTo(default(DateTimeOffset));
            records[index] = new(scenario.Stream, revision, revision, data, actual.Events[index].RecordedAt);
        }
        await SqlClusterProtocol.EqualAsync(new StreamPage(scenario.Stream,
            new(tail, McpEventStreamTokens.FirstEventRevision, McpEventStreamTokens.StreamGeneration),
            [.. records], actual.CutPosition, false, null, actual.CutPosition), actual);
    }

    internal static async Task HistoryAsync(RequestCqrsClusterCallers callers,
        McpEventStreamScenario scenario, long tail, long minimum, CancellationToken token)
    {
        var expected = tail == StreamCatchupColdClusterFlow.CatchupRevision
            ? McpEventStreamScenario.InputEvents.ToArray()
            : McpEventStreamScenario.InputEvents.Append(StreamCatchupColdClusterFlow.TailEvent).ToArray();
        await RoutesAsync(callers, scenario, scenario.ReadRequest(McpEventStreamTokens.InitialRevision,
            McpEventStreamTokens.ExpectedEventCount + McpEventStreamTokens.FirstEventRevision), expected, tail, minimum, token);
    }

    internal static async Task ContinuationAsync(RequestCqrsClusterCallers callers,
        McpEventStreamScenario scenario, CommitReceipt tail, CancellationToken token)
    {
        await RoutesAsync(callers, scenario, scenario.ReadRequest(StreamCatchupColdClusterFlow.CatchupRevision,
            McpEventStreamTokens.EmptyTailLimit), [StreamCatchupColdClusterFlow.TailEvent],
            StreamCatchupColdClusterFlow.TailRevision, tail.Token.Position, token);
        await RoutesAsync(callers, scenario, scenario.ReadRequest(StreamCatchupColdClusterFlow.TailRevision,
            McpEventStreamTokens.EmptyTailLimit), [], StreamCatchupColdClusterFlow.TailRevision, tail.Token.Position, token);
    }

    private static async Task RoutesAsync(RequestCqrsClusterCallers callers,
        McpEventStreamScenario scenario, ReadStreamRequest request, EventData[] expected,
        long tail, long minimum, CancellationToken token)
    {
        await PageAsync(scenario, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReadStreamAsync(request, token)), tail, expected, minimum);
        await PageAsync(scenario, (await McpCallerAssertions.SuccessAsync<StreamPage>(
            await callers.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token))).Value, tail, expected, minimum);
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request);
        await PageAsync(scenario, await SqlClusterProtocol.SdkAsync<StreamPage>(callers.Sdk, sql, token), tail, expected, minimum);
        await PageAsync(scenario, await SqlClusterProtocol.McpAsync<StreamPage>(callers.Mcp, sql, token), tail, expected, minimum);
    }

    internal static async Task DeniedThenHealthyAsync(RequestCqrsClusterCallers callers,
        McpEventStreamScenario scenario, CommitReceipt tail, CancellationToken token)
    {
        var request = scenario.ReadRequest(StreamCatchupColdClusterFlow.CatchupRevision,
            McpEventStreamTokens.EmptyTailLimit, McpEventStreamTokens.StaleGeneration);
        var sdk = await callers.Sdk.ReadStreamAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.StreamsRead,
            request, token), ErrorCode.TokenInvalidated, dispatched: true)).Value;
        await LivePageClusterAssertions.DeniedSqlAsync(callers,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request), ErrorCode.TokenInvalidated, token);
        await ContinuationAsync(callers, scenario, tail, token);
    }
}
