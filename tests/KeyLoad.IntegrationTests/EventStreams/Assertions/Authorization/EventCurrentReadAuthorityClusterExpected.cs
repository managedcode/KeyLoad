using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventCurrentReadAuthorityClusterExpected
{
    internal const string ScopeDeniedDetail = "The principal cannot perform this operation in this scope.";
    internal const string AppendKind = "appendEvents";
    internal const string CursorExpiredDetail = "The stream traversal cursor expired or changed scope.";
    private const string FourthId = "r42-current-read-fourth";
    private const string FourthPayload = "{\"step\":4}";
    internal static EventData Fourth => McpEventStreamScenario.ExpectedEvents[0] with
    {
        EventId = FourthId,
        PayloadJson = FourthPayload
    };

    internal static EventData[] Data(bool updated) => updated
        ? [.. McpEventStreamScenario.ExpectedEvents, Fourth]
        : McpEventStreamScenario.ExpectedEvents.ToArray();

    internal static async Task PageAsync(EventCurrentReadAuthorityClusterState state,
        StreamPage page, EventData[] expected, long first, long head,
        long? snapshot, bool more)
    {
        await Assert.That(page.Stream).IsEqualTo(state.Scenario.Stream);
        await Assert.That(page.Head.TailRevision).IsEqualTo(head);
        await Assert.That(page.Head.FirstAvailableRevision).IsEqualTo(1L);
        await Assert.That(page.Head.Generation).IsEqualTo(1L);
        await Assert.That(page.Events.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            var actual = page.Events[index];
            await Assert.That(actual.Stream).IsEqualTo(state.Scenario.Stream);
            await Assert.That(actual.Revision).IsEqualTo(first + index);
            await Assert.That(actual.EventSequence).IsEqualTo(first + index);
            await SqlClusterProtocol.EqualAsync(expected[index], actual.Data);
            await Assert.That(actual.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
        }
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.SnapshotCutPosition).IsEqualTo(snapshot ?? page.CutPosition);
        await Assert.That(page.HasMore).IsEqualTo(more);
        await Assert.That(page.Cursor is not null).IsEqualTo(more);
    }
}
