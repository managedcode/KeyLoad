using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDueDatabaseScopeClusterExpected
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static MessageInspection Scheduled(QueueDueDatabaseScopeClusterLane lane, DateTimeOffset due)
        => new(new(QueueDueDatabaseScopeClusterProtocol.Message, MessageState.Scheduled, 0, 1L, 0L,
            due, null), lane.Payload, QueueDeadlineClusterProtocol.Headers);

    internal static MessageInspection Ready(QueueDueDatabaseScopeClusterLane lane, bool healthy)
        => new(new(healthy ? QueueDueDatabaseScopeClusterProtocol.Healthy : QueueDueDatabaseScopeClusterProtocol.Message,
            MessageState.Ready, 0, healthy ? 1L : 2L, healthy ? 2L : 1L, null, null),
            lane.Payload, QueueDeadlineClusterProtocol.Headers);

    internal static async Task ReceiptAsync(CommitReceipt actual, Guid id,
        QueueDueDatabaseScopeClusterLane lane, MutationReceipt[] mutations)
    {
        await Assert.That(actual.CommandId).IsEqualTo(id);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(lane.Lane.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(actual.PersistedCopies).IsEqualTo(3);
        await EqualAsync(actual.Mutations.ToArray(), mutations);
    }

    internal static async Task MessageAsync(RequestCqrsClusterCallers admin, QueueLaneRef lane,
        string id, MessageInspection? expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, id);
        await EqualAsync(await PublicReadAssertions.MessageAsync(await admin.Sdk.InspectAsync(request, token)), expected);
        await EqualAsync((await PublicReadAssertions.MessageAsync(await admin.Mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }

    /// <summary>One committed queue mutation appends exactly one partition-local outbox entry with its effect.</summary>
    internal static async Task OutboxAdvancedAsync(RequestCqrsClusterCallers admin,
        QueueDueDatabaseScopeClusterLane lane, CancellationToken token)
    {
        var previous = lane.Outbox;
        var actual = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(lane.Lane.Partition, token));
        await Assert.That(actual.Head.Tail).IsEqualTo(previous.Head.Tail + 1L);
        await Assert.That(actual.Head.FirstAvailable).IsEqualTo(previous.Head.FirstAvailable);
        await Assert.That(actual.Head.StoredRecords).IsEqualTo(previous.Head.StoredRecords + 1L);
        await Assert.That(actual.Head.StoredBytes).IsGreaterThan(previous.Head.StoredBytes);
        await EqualAsync(actual.Consumers, previous.Consumers);
        lane.Outbox = actual;
    }
}
