using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

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
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
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
}
