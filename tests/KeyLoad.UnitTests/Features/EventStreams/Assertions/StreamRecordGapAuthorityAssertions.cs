using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class StreamRecordGapAuthorityAssertions
{
    private const string Invalid = "The stream traversal request is invalid.";

    internal static async Task RequireAsync(EventAppendWholeFlowFixture fixture, long missing,
        StreamPage forward, StreamPage backward, CancellationToken token)
    {
        var position = fixture.Database.Store.Position;
        var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        foreach (var direction in new[] { StreamReadDirection.Forward, StreamReadDirection.Backward })
        {
            await RefusalAsync(fixture, direction, null, token);
            var cursor = direction == StreamReadDirection.Forward ? forward : backward;
            if (direction == StreamReadDirection.Forward && missing == 1L)
            {
                await StreamRecordKeyAuthorityAssertions.PageAsync(fixture.Database.Database, fixture.Stream,
                fixture.Now, direction, cursor.Cursor, cursor.SnapshotCutPosition, [2L, 3L], 3L, token);
            }
            else if (direction == StreamReadDirection.Backward && missing == 3L)
            {
                await StreamRecordKeyAuthorityAssertions.PageAsync(fixture.Database.Database, fixture.Stream,
                fixture.Now, direction, cursor.Cursor, cursor.SnapshotCutPosition, [2L, 1L], 3L, token);
            }
            else
            { await RefusalAsync(fixture, direction, cursor.Cursor, token); }
        }
        var empty = fixture.Database.Database.ReadStream(EventAppendWholeFlowFixture.Root,
            new ReadStreamRequest(fixture.Stream, AfterRevision: 3L), token);
        var expected = new StreamPage(fixture.Stream, new(3L, 1L, 1L), [], position, false, null, position);
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(empty, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store))
            .IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static async Task RefusalAsync(EventAppendWholeFlowFixture fixture, StreamReadDirection direction,
        string? cursor, CancellationToken token)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            StreamRecordKeyAuthorityAssertions.Read(fixture.Database.Database, fixture.Stream, direction, 100, cursor, token));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(Invalid);
    }
}
