using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDueDatabaseScopeRf3Expected
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static MessageInspection Scheduled(QueueDueDatabaseScopeRf3Lane lane, DateTimeOffset due)
        => new(new(QueueDueDatabaseScopeRf3Protocol.Message, MessageState.Scheduled, 0, 1L, 0L,
            due, null), lane.Payload, QueueDeadlineRf3Protocol.Headers);

    internal static MessageInspection Ready(QueueDueDatabaseScopeRf3Lane lane, bool healthy)
        => new(new(healthy ? QueueDueDatabaseScopeRf3Protocol.Healthy : QueueDueDatabaseScopeRf3Protocol.Message,
            MessageState.Ready, 0, healthy ? 1L : 2L, healthy ? 2L : 1L, null, null),
            lane.Payload, QueueDeadlineRf3Protocol.Headers);

    internal static async Task ReceiptAsync(CommitReceipt actual, Guid id,
        QueueDueDatabaseScopeRf3Lane lane, MutationReceipt[] mutations)
    {
        await Assert.That(actual.CommandId).IsEqualTo(id);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(lane.Lane.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Incarnation).IsEqualTo(lane.Placement.Incarnation);
        await Assert.That(actual.Token.OwnershipEpoch).IsEqualTo(lane.Placement.PlacementEpoch);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await EqualAsync(actual.Mutations.ToArray(), mutations);
    }

    internal static async Task MessageAsync(RequestCqrsRf3Callers admin, QueueLaneRef lane,
        string id, MessageInspection? expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, id);
        await EqualAsync(await PublicReadAssertions.MessageAsync(await admin.Sdk.InspectAsync(request, token)), expected);
        await EqualAsync((await PublicReadAssertions.MessageAsync(await admin.Mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }
}
