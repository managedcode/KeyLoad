using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class EventByteBoundPublicExpected
{
    internal const string BytesDetail = "The read result byte budget is exceeded.";
    internal static ReadStreamRequest Whole(EventByteBoundPublicState state)
        => new(state.Scenario.Stream, Limit: 3);
    internal static ReadStreamRequest First(EventByteBoundPublicState state, StreamReadDirection direction, int bytes)
        => new(state.Scenario.Stream, Limit: 1, Direction: direction, MaxBytes: bytes);
    internal static ReadStreamRequest Resume(EventByteBoundPublicState state,
        StreamReadDirection direction, StreamPage captured, int bytes)
        => new(state.Scenario.Stream, Direction: direction, MaxBytes: bytes, Cursor: captured.Cursor);

    internal static async Task PageAsync(EventByteBoundPublicState state, StreamPage page,
        int[] indices, bool more, long? snapshot = null)
    {
        await SqlClusterProtocol.EqualAsync(state.Scenario.Stream, page.Stream);
        await SqlClusterProtocol.EqualAsync(new StreamHead(3L, 1L, 1L), page.Head);
        await SqlClusterProtocol.EqualAsync(indices.Select(index => state.Original.Events[index]).ToArray(), page.Events.ToArray());
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
