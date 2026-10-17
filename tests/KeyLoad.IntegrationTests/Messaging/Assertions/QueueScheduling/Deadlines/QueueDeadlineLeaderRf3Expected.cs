
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderRf3Expected
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static MessageInspection Scheduled(DateTimeOffset due)
        => new(new(QueueDeadlineLeaderRf3Protocol.Message, MessageState.Scheduled, 0, 1L, 0L, due, null),
            QueueDeadlineRf3Protocol.Payload, QueueDeadlineRf3Protocol.Headers);

    internal static MessageInspection Ready(bool healthy)
        => new(new(healthy ? QueueDeadlineLeaderRf3Protocol.Healthy : QueueDeadlineLeaderRf3Protocol.Message,
            MessageState.Ready, 0, healthy ? 1L : 2L, healthy ? 2L : 1L, null, null),
            QueueDeadlineRf3Protocol.Payload, QueueDeadlineRf3Protocol.Headers);

    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid id, QueueDeadlineLeaderRf3State state,
        MutationReceipt[] expected)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Lane.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await EqualAsync(receipt.Mutations.ToArray(), expected);
    }
}
