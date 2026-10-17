using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineNativeAssertions
{
    internal static async Task OriginalAsync(DatabaseEngine database, QueueLaneRef lane, MessageMetadata expected)
    {
        var actual = database.InspectMessage(QueueDeadlineNativeProtocol.Root,
            lane, QueueDeadlineNativeProtocol.Message);
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Metadata).IsEqualTo(expected);
        await Assert.That(actual.PayloadJson).IsEqualTo(expected.State == MessageState.Expired ? null : QueueDeadlineNativeProtocol.Payload);
        await Assert.That(actual.HeadersJson).IsEqualTo(expected.State == MessageState.Expired ? null : QueueDeadlineNativeProtocol.Headers);
    }

    internal static async Task RefusedAsync(DatabaseEngine database, ZoneTreeStore store, QueueLaneRef lane,
        CommandRequest command, DateTimeOffset time, ErrorCode error)
    {
        var original = QueueRetryColdAssertions.LaneBytes(store, lane);
        var result = QueueDeadlineNativeOperations.Batch(database, command, time);
        await Assert.That(result.Error).IsEqualTo(error);
        await Assert.That(QueueRetryColdAssertions.LaneBytes(store, lane))
            .IsEquivalentTo(original, CollectionOrdering.Matching);
    }
}
