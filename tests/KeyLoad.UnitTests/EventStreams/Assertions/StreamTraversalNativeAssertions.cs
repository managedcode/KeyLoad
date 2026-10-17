using System.Text.Json;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class StreamTraversalNativeAssertions
{
    private const string PublicPayload = "{\"public\":\"public-stream-value\"}";
    private const string PublicHeaders = "{\"kind\":\"created\"}";
    internal static async Task PageAsync(StreamPage page, long[] revisions, long tail, bool more)
    {
        await Assert.That(page.Head).IsEqualTo(new StreamHead(tail, StreamTraversalTestProtocol.First, StreamTraversalTestProtocol.First));
        await Assert.That(page.HasMore).IsEqualTo(more);
        await Assert.That(page.Events.Length).IsEqualTo(revisions.Length);
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(page.SnapshotCutPosition);
        await Assert.That(page.SnapshotCutPosition).IsGreaterThan(StreamTraversalTestProtocol.Empty);
        for (var index = StreamTraversalTestProtocol.Empty; index < revisions.Length; index++)
        {
            var actual = page.Events[index];
            var expected = new EventRecord(page.Stream, revisions[index], revisions[index],
                new(revisions[index].ToString(System.Globalization.CultureInfo.InvariantCulture), "Created", PublicPayload, PublicHeaders),
                actual.RecordedAt);
            await Assert.That(actual.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
            await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        }
        await Assert.That(page.Cursor is not null).IsEqualTo(more);
    }

    internal static async Task UnchangedAsync(StreamReadResourceFixture fixture, (long Position, string[] Image) before)
    {
        var after = fixture.Cut();
        await Assert.That(after.Position).IsEqualTo(before.Position);
        await Assert.That(after.Image.SequenceEqual(before.Image, StringComparer.Ordinal)).IsTrue();
    }
}
