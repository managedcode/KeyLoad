using KeyLoad.IntegrationTests.Features.ChangeFeeds;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class StreamCatchupRf3Assertions
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
            var revision = data.EventId == StreamCatchupColdRf3Flow.TailEvent.EventId
                ? StreamCatchupColdRf3Flow.TailRevision
                : McpEventStreamScenario.ExpectedEvents.IndexOf(data) + McpEventStreamTokens.FirstEventRevision;
            await Assert.That(actual.Events[index].RecordedAt).IsNotEqualTo(default(DateTimeOffset));
            records[index] = new(scenario.Stream, revision, revision, data, actual.Events[index].RecordedAt);
        }
        await SqlRf3Protocol.EqualAsync(new StreamPage(scenario.Stream,
            new(tail, McpEventStreamTokens.FirstEventRevision, McpEventStreamTokens.StreamGeneration),
            [.. records], actual.CutPosition, false, null, actual.CutPosition), actual);
    }

    internal static async Task HistoryAsync(RequestCqrsRf3Callers callers,
        McpEventStreamScenario scenario, long tail, long minimum, CancellationToken token)
    {
        var expected = tail == StreamCatchupColdRf3Flow.CatchupRevision
            ? McpEventStreamScenario.ExpectedEvents.ToArray()
            : McpEventStreamScenario.ExpectedEvents.Append(StreamCatchupColdRf3Flow.TailEvent).ToArray();
        await RoutesAsync(callers, scenario, scenario.ReadRequest(McpEventStreamTokens.InitialRevision,
            McpEventStreamTokens.ExpectedEventCount + McpEventStreamTokens.FirstEventRevision), expected, tail, minimum, token);
    }

    internal static async Task ContinuationAsync(RequestCqrsRf3Callers callers,
        McpEventStreamScenario scenario, CommitReceipt tail, CancellationToken token)
    {
        await RoutesAsync(callers, scenario, scenario.ReadRequest(StreamCatchupColdRf3Flow.CatchupRevision,
            McpEventStreamTokens.EmptyTailLimit), [StreamCatchupColdRf3Flow.TailEvent],
            StreamCatchupColdRf3Flow.TailRevision, tail.Token.Position, token);
        await RoutesAsync(callers, scenario, scenario.ReadRequest(StreamCatchupColdRf3Flow.TailRevision,
            McpEventStreamTokens.EmptyTailLimit), [], StreamCatchupColdRf3Flow.TailRevision, tail.Token.Position, token);
    }

    private static async Task RoutesAsync(RequestCqrsRf3Callers callers,
        McpEventStreamScenario scenario, ReadStreamRequest request, EventData[] expected,
        long tail, long minimum, CancellationToken token)
    {
        await PageAsync(scenario, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReadStreamAsync(request, token)), tail, expected, minimum);
        await PageAsync(scenario, (await McpCallerAssertions.SuccessAsync<StreamPage>(
            await callers.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token))).Value, tail, expected, minimum);
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request);
        await PageAsync(scenario, await SqlRf3Protocol.SdkAsync<StreamPage>(callers.Sdk, sql, token), tail, expected, minimum);
        await PageAsync(scenario, await SqlRf3Protocol.McpAsync<StreamPage>(callers.Mcp, sql, token), tail, expected, minimum);
    }

    internal static async Task DeniedThenHealthyAsync(RequestCqrsRf3Callers callers,
        McpEventStreamScenario scenario, CommitReceipt tail, CancellationToken token)
    {
        var request = scenario.ReadRequest(StreamCatchupColdRf3Flow.CatchupRevision,
            McpEventStreamTokens.EmptyTailLimit, McpEventStreamTokens.StaleGeneration);
        var sdk = await callers.Sdk.ReadStreamAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.StreamsRead,
            request, token), ErrorCode.TokenInvalidated, dispatched: true);
        await LivePageRf3Assertions.DeniedSqlAsync(callers,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.StreamsRead, request), ErrorCode.TokenInvalidated, token);
        await ContinuationAsync(callers, scenario, tail, token);
    }
}
