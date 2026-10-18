using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventSchemaCursorPublicExpected
{
    internal const string HeaderPath = "/source";
    internal const string Classification = "event-source-sensitive";
    internal const string RawReadGrant = "event.source.read";
    internal const string ExpiredDetail = "The stream traversal cursor expired or changed scope.";
    internal const string SafeMcpDetail = "The database operation could not be completed.";
    private static readonly string[] ProjectedHeaders = ["{\"sequence\":1}", "{\"sequence\":2}", "{\"sequence\":3}"];
    internal static ReadStreamRequest Whole(EventSchemaCursorPublicState state) => new(state.Scenario.Stream, Limit: 3);
    internal static ReadStreamRequest First(EventSchemaCursorPublicState state, StreamReadDirection direction)
        => new(state.Scenario.Stream, Limit: 1, Direction: direction);
    internal static ReadStreamRequest Resume(EventSchemaCursorPublicState state,
        StreamReadDirection direction, StreamPage page)
        => new(state.Scenario.Stream, Direction: direction, Cursor: page.Cursor);

    internal static async Task PageAsync(EventSchemaCursorPublicState state, StreamPage page,
        int[] indices, bool reader, bool more, long? snapshot = null)
    {
        var expected = indices.Select(index => reader && state.Definition.SchemaVersion == 2L
            ? state.Original.Events[index] with
            {
                Data = state.Original.Events[index].Data with
                {
                    HeadersJson = ProjectedHeaders[index]
                }
            }
            : state.Original.Events[index]).ToArray();
        await SqlClusterProtocol.EqualAsync(state.Scenario.Stream, page.Stream);
        await SqlClusterProtocol.EqualAsync(new StreamHead(3L, 1L, 1L), page.Head);
        await SqlClusterProtocol.EqualAsync(expected, page.Events.ToArray());
        await Assert.That(page.HasMore).IsEqualTo(more);
        if (more)
        {
            await Assert.That(page.Cursor).IsNotNull();
        }
        else
        {
            await Assert.That(page.Cursor).IsNull();
        }
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.SnapshotCutPosition).IsEqualTo(snapshot ?? page.CutPosition);
    }

    internal static async Task OriginalAsync(McpEventStreamScenario scenario, StreamPage page)
    {
        await SqlClusterProtocol.EqualAsync(scenario.Stream, page.Stream);
        await SqlClusterProtocol.EqualAsync(new StreamHead(3L, 1L, 1L), page.Head);
        await Assert.That(page.Events.Length).IsEqualTo(3);
        for (var index = 0; index < 3; index++)
        {
            await SqlClusterProtocol.EqualAsync(scenario.Stream, page.Events[index].Stream);
            await Assert.That(page.Events[index].Revision).IsEqualTo(index + 1L);
            await Assert.That(page.Events[index].EventSequence).IsEqualTo(index + 1L);
            await SqlClusterProtocol.EqualAsync(McpEventStreamScenario.ExpectedEvents[index], page.Events[index].Data);
        }
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.SnapshotCutPosition).IsEqualTo(page.CutPosition);
    }
}
