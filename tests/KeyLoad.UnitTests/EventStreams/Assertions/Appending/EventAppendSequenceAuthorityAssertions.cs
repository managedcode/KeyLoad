using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventAppendSequenceAuthorityAssertions
{
    internal static async Task ReceiptAsync(EventAppendWholeFlowFixture fixture, CommandRequest command,
        Result<object> result, long revision)
    {
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Position).IsEqualTo(fixture.Database.Store.Position);
        var expected = new MutationReceipt[]
        {
            new("putDocument", EventAppendWholeFlowFixture.Documents, ((PutDocument)command.Mutations[0]).Id, 1L),
            new("enqueue", EventAppendWholeFlowFixture.Queue, ((EnqueueMessage)command.Mutations[1]).MessageId, 1L),
            new("appendEvents", EventAppendWholeFlowFixture.Streams, EventAppendWholeFlowFixture.StreamId, revision)
        };
        await Assert.That(receipt.Mutations).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task PageAsync(DatabaseEngine database, StreamRef stream, DateTimeOffset now, CancellationToken token)
    {
        var expectedData = new[]
        {
            EventAppendWholeFlowFixture.Data(EventAppendWholeFlowFixture.InitialId),
            EventAppendWholeFlowFixture.Data(EventAppendSequenceAuthorityFlow.Event),
            EventAppendWholeFlowFixture.Data(EventAppendSequenceAuthorityFlow.HealthyEvent)
        };
        var expected = new StreamPage(stream, new(3L, 1L, 1L),
            [.. expectedData.Select((data, index) => new EventRecord(stream, index + 1L, index + 1L, data, now))],
            database.Store.Position, false, null, database.Store.Position);
        var actual = database.ReadStream(EventAppendWholeFlowFixture.Root, stream, cancellationToken: token);
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, CommandRequest command,
        Result<object> original, DateTimeOffset now)
    {
        var position = store.Position;
        var before = QueueWholeFlowStorage.Bytes(store);
        var actual = QueueWholeFlowStorage.Apply(database, OperationKind.Batch, command, command.CommandId, now);
        await NativeReplayResultAssertions.Same<CommitReceipt>(actual, original);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store))
            .IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
