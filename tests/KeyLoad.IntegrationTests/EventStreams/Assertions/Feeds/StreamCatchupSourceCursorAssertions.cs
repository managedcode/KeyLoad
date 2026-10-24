using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class StreamCatchupSourceCursorAssertions
{
    internal static async Task<ReadEventSourceRequest> CaptureAsync(RequestCqrsClusterCallers callers,
        McpEventStreamScenario scenario, CommitReceipt original, CancellationToken token)
    {
        var source = new EventSourceRef(scenario.Partition, McpEventStreamTokens.StreamSet,
            EventSourceKind.Stream, McpEventStreamTokens.StreamId, McpEventStreamTokens.StreamGeneration);
        var request = new ReadEventSourceRequest(source, AfterPosition: StreamCatchupColdClusterFlow.CatchupRevision);
        var page = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(request, token));
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(original.Token.Position);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        await SqlClusterProtocol.EqualAsync(new EventSourcePage(source,
            new(StreamCatchupColdClusterFlow.CatchupRevision, McpEventStreamTokens.FirstEventRevision,
                McpEventStreamTokens.StreamGeneration), [], page.Cursor, page.CutPosition, false), page);
        return new(source, Cursor: page.Cursor);
    }

    internal static async Task RequireAsync(RequestCqrsClusterCallers callers,
        ReadEventSourceRequest original, CommitReceipt tail, CancellationToken token)
    {
        await RouteAsync(original, tail, async request => await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReadEventSourceAsync(request, token)));
        await RouteAsync(original, tail, async request => (await McpCallerAssertions.SuccessAsync<EventSourcePage>(
            await callers.Mcp.CallAsync(McpCallerTools.EventsRead, request, token))).Value);
        await RouteAsync(original, tail, request => SqlClusterProtocol.SdkAsync<EventSourcePage>(callers.Sdk,
            SqlClusterProtocol.Call(original.Source.Partition, McpCallerTools.EventsRead, request), token));
        await RouteAsync(original, tail, request => SqlClusterProtocol.McpAsync<EventSourcePage>(callers.Mcp,
            SqlClusterProtocol.Call(original.Source.Partition, McpCallerTools.EventsRead, request), token));
    }

    private static async Task RouteAsync(ReadEventSourceRequest original, CommitReceipt tail,
        Func<ReadEventSourceRequest, Task<EventSourcePage>> read)
    {
        var page = await read(original);
        await Assert.That(page.Events).HasSingleItem();
        await Assert.That(page.Events[0].RecordedAt).IsNotEqualTo(default(DateTimeOffset));
        var expected = new SourceEventRecord(original.Source, StreamCatchupColdClusterFlow.TailRevision,
            StreamCatchupColdClusterFlow.TailRevision, StreamCatchupColdClusterFlow.TailEvent, page.Events[0].RecordedAt);
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(tail.Token.Position);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        var head = new EventSourceHead(StreamCatchupColdClusterFlow.TailRevision,
            McpEventStreamTokens.FirstEventRevision, McpEventStreamTokens.StreamGeneration);
        await SqlClusterProtocol.EqualAsync(new EventSourcePage(original.Source, head, [expected],
            page.Cursor, page.CutPosition, false), page);
        var empty = await read(original with
        {
            Cursor = page.Cursor
        });
        await Assert.That(empty.CutPosition).IsGreaterThanOrEqualTo(page.CutPosition);
        await Assert.That(string.IsNullOrEmpty(empty.Cursor)).IsFalse();
        await SqlClusterProtocol.EqualAsync(new EventSourcePage(original.Source, head, [],
            empty.Cursor, empty.CutPosition, false), empty);
    }
}
