using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineAssertions
{
    internal static async Task OriginalAsync(DatabaseEngine database, QueueLaneRef lane, MessageMetadata expected)
    {
        var actual = database.InspectMessage(RuntimeQueueDeadlineProtocol.Root,
            lane, RuntimeQueueDeadlineProtocol.Message);
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Metadata).IsEqualTo(expected);
        await Assert.That(actual.PayloadJson).IsEqualTo(expected.State == MessageState.Expired ? null : RuntimeQueueDeadlineProtocol.Payload);
        await Assert.That(actual.HeadersJson).IsEqualTo(expected.State == MessageState.Expired ? null : RuntimeQueueDeadlineProtocol.Headers);
    }

    internal static async Task RefusedAsync(DatabaseEngine database, ZoneTreeStore store, QueueLaneRef lane,
        CommandRequest command, DateTimeOffset time, ErrorCode error)
    {
        var original = QueueRetryColdAssertions.LaneBytes(store, lane);
        var result = QueueDeadlineOperations.Batch(database, command, time);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(error);
        await Assert.That(QueueRetryColdAssertions.LaneBytes(store, lane))
            .IsEquivalentTo(original, CollectionOrdering.Matching);
    }

    internal static async Task AlteredRetryDrawAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueLaneRef lane, Command<DatabaseCommand> operation)
    {
        var altered = database.OwnCommand(operation);
        var payload = altered.Value!;
        var choices = OrleansSerialization.Deserialize<QueueRetryDecisions>(payload.RetryDecisions.Span);
        var original = choices.Items.Single();
        var delay = original.DelayMilliseconds == RuntimeQueueDeadlineProtocol.RetryCapMilliseconds
            ? (int)RuntimeQueueDeadlineProtocol.First
            : original.DelayMilliseconds + (int)RuntimeQueueDeadlineProtocol.First;
        altered.Value = payload with
        {
            RetryDecisions = OrleansSerialization.Serialize(choices with
            {
                Items = [original with { DelayMilliseconds = delay, RetryAt = altered.Timestamp.AddMilliseconds(delay) }]
            })
        };
        var position = store.Position;
        var applied = database.LastApplied;
        var bytes = QueueRetryColdAssertions.LaneBytes(store, lane);
        await Assert.That(database.ReadStoredCommand(operation, CancellationToken.None)).IsNull();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(altered));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(database.LastApplied).IsEqualTo(applied);
        await Assert.That(QueueRetryColdAssertions.LaneBytes(store, lane))
            .IsEquivalentTo(bytes, CollectionOrdering.Matching);
        await Assert.That(database.ReadStoredCommand(operation, CancellationToken.None)).IsNull();
    }
}
