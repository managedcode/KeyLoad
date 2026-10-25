using KeyLoad.FunctionalTests.Messaging;
using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests.EventStreams;

internal static class StreamRecordKeyAuthorityAssertions
{
    private const string Invalid = "The stream traversal request is invalid.";

    internal static async Task ReceiptAsync(EventAppendWholeFlowFixture fixture, CommandRequest command, Result<object> result, long tail)
    {
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Position).IsEqualTo(fixture.Database.Database.LastApplied);
        await Assert.That(receipt.Mutations).IsEquivalentTo(
            new MutationReceipt[] { new("appendEvents", EventAppendWholeFlowFixture.Streams,
                EventAppendWholeFlowFixture.StreamId, tail) }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static StreamPage Read(DatabaseEngine database, StreamRef stream, StreamReadDirection direction,
        int limit, string? cursor, CancellationToken token)
        => database.ReadStream(EventAppendWholeFlowFixture.Root,
            new ReadStreamRequest(stream, Limit: limit, Direction: direction, Cursor: cursor), token);

    internal static async Task RefusalsAsync(EventAppendWholeFlowFixture fixture, StreamPage forward,
        StreamPage backward, CancellationToken token)
    {
        var position = fixture.Database.Store.Position;
        var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        foreach (var direction in new[] { StreamReadDirection.Forward, StreamReadDirection.Backward })
        {
            foreach (var cursor in new string?[] { null, direction == StreamReadDirection.Forward ? forward.Cursor : backward.Cursor })
            {
                var error = Assert.ThrowsExactly<KeyLoadException>(() => Read(fixture.Database.Database,
                    fixture.Stream, direction, 100, cursor, token));
                await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
                await Assert.That(error.Message).IsEqualTo(Invalid);
                await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
                await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store))
                    .IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            }
        }
    }

    internal static async Task PagesAsync(DatabaseEngine database, StreamRef stream, DateTimeOffset now,
        StreamPage forward, StreamPage backward, CancellationToken token)
    {
        await PageAsync(database, stream, now, StreamReadDirection.Forward, null, database.LastApplied, [1L, 2L, 3L], 3L, token);
        await PageAsync(database, stream, now, StreamReadDirection.Backward, null, database.LastApplied, [3L, 2L, 1L], 3L, token);
        await PageAsync(database, stream, now, StreamReadDirection.Forward, forward.Cursor, forward.SnapshotCutPosition, [2L, 3L], 3L, token);
        await PageAsync(database, stream, now, StreamReadDirection.Backward, backward.Cursor, backward.SnapshotCutPosition, [2L, 1L], 3L, token);
    }

    internal static async Task PagesFourAsync(DatabaseEngine database, StreamRef stream, DateTimeOffset now,
        StreamPage forward, StreamPage backward, CancellationToken token)
    {
        await PageAsync(database, stream, now, StreamReadDirection.Forward, null, database.LastApplied, [1L, 2L, 3L, 4L], 4L, token);
        await PageAsync(database, stream, now, StreamReadDirection.Backward, null, database.LastApplied, [4L, 3L, 2L, 1L], 4L, token);
        await PageAsync(database, stream, now, StreamReadDirection.Forward, forward.Cursor, forward.SnapshotCutPosition, [2L, 3L], 3L, token);
        await PageAsync(database, stream, now, StreamReadDirection.Backward, backward.Cursor, backward.SnapshotCutPosition, [2L, 1L], 3L, token);
    }

    internal static async Task PageAsync(DatabaseEngine database, StreamRef stream, DateTimeOffset now,
        StreamReadDirection direction, string? cursor, long snapshotCut, long[] revisions, long tail, CancellationToken token)
    {
        var ids = new[] { EventAppendWholeFlowFixture.InitialId, StreamRecordKeyAuthorityFlow.Second,
            StreamRecordKeyAuthorityFlow.Third, StreamRecordKeyAuthorityFlow.Fourth };
        var records = revisions.Select(revision => new EventRecord(stream, revision, revision,
            EventAppendWholeFlowFixture.Data(ids[checked((int)revision - 1)]), now));
        var expected = new StreamPage(stream, new(tail, 1L, 1L), [.. records], database.LastApplied, false, null, snapshotCut);
        var position = database.Store.Position;
        var before = QueueWholeFlowStorage.Bytes((KeyLoad.Storage.ZoneTree.ZoneTreeStore)database.Store);
        var actual = Read(database, stream, direction, 100, cursor, token);
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes((KeyLoad.Storage.ZoneTree.ZoneTreeStore)database.Store))
            .IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
