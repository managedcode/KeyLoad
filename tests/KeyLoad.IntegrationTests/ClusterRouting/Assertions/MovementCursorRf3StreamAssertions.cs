namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MovementCursorRf3StreamAssertions
{
    private const int EventOffset = 0;
    private const int SingleEvent = 1;
    private const long FirstRevision = 1;
    private const long SecondRevision = 2;
    private const long EmptyCut = 0;

    internal static async Task FirstAsync(StreamPage page, StreamRef stream)
    {
        await Assert.That(page.Stream).IsEqualTo(stream);
        await Assert.That(page.Head).IsEqualTo(new StreamHead(SecondRevision, FirstRevision, stream.Generation));
        await Assert.That(page.Events.Length).IsEqualTo(SingleEvent);
        await EventAsync(page.Events[EventOffset], stream, FirstRevision, MovementCursorRf3Stream.First);
        await Assert.That(page.HasMore).IsTrue();
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        await Assert.That(page.SnapshotCutPosition).IsGreaterThan(EmptyCut);
    }

    internal static async Task SecondAsync(StreamPage page, StreamPage original)
    {
        await Assert.That(page.Stream).IsEqualTo(original.Stream);
        await Assert.That(page.Head).IsEqualTo(original.Head);
        await Assert.That(page.Events.Length).IsEqualTo(SingleEvent);
        await EventAsync(page.Events[EventOffset], original.Stream, SecondRevision, MovementCursorRf3Stream.Second);
        await Assert.That(page.Events[EventOffset].EventSequence)
            .IsGreaterThan(original.Events[EventOffset].EventSequence);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.SnapshotCutPosition).IsEqualTo(original.SnapshotCutPosition);
    }

    private static async Task EventAsync(EventRecord actual, StreamRef stream, long revision, EventData data)
    {
        await Assert.That(actual.Stream).IsEqualTo(stream);
        await Assert.That(actual.Revision).IsEqualTo(revision);
        await Assert.That(actual.Data).IsEqualTo(data);
        await Assert.That(actual.EventSequence).IsGreaterThan(EmptyCut);
    }
}
