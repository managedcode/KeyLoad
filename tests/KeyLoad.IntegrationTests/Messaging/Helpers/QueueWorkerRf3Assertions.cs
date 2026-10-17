using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3Assertions
{
    internal static Task EqualAsync<T>(T actual, T expected) => QueueLeaseRf3Assertions.EqualAsync(actual, expected);

    internal static async Task<MessageInspection?> ImageAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueLaneRef lane, string id, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, id);
        var image = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(request, token));
        await EqualAsync((await PublicReadAssertions.MessageAsync(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value, image);
        return image;
    }

    internal static async Task ReadyAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        string id, long sequence, CancellationToken token)
        => await EqualAsync(await ImageAsync(sdk, mcp, seed.Lane, id, token),
            new MessageInspection(new(id, MessageState.Ready, QueueWorkerRf3Protocol.NoAttempts,
                QueueWorkerRf3Protocol.ReadyStateVersion, sequence, null, null, DeliveryGeneration: QueueWorkerRf3Protocol.InitialDeliveryGeneration), QueueWorkerRf3Protocol.Payload,
                QueueWorkerRf3Protocol.Headers));

    internal static async Task LeasedAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        QueueWorkerDeliveryContext context, long sequence, CancellationToken token)
    {
        var delivery = context.Delivery;
        await QueueWorkerRf3Claims.RequireAsync(seed, delivery);
        await EqualAsync(delivery, new Delivery(delivery.Id, QueueWorkerRf3Protocol.Payload,
            QueueWorkerRf3Protocol.Headers, delivery.Token, QueueWorkerRf3Protocol.InitialLeaseVersion,
            delivery.LeaseUntil, QueueWorkerRf3Protocol.ClaimAttempt, QueueWorkerRf3Protocol.InitialDeliveryGeneration));
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        await EqualAsync(await ImageAsync(sdk, mcp, seed.Lane, delivery.Id, token),
            new MessageInspection(new(delivery.Id, MessageState.Leased, QueueWorkerRf3Protocol.ClaimAttempt,
                QueueWorkerRf3Protocol.ClaimStateVersion, sequence, null, null, seed.Identity.Principal.Id,
                QueueWorkerRf3Protocol.InitialLeaseVersion, delivery.LeaseUntil, DeliveryGeneration: QueueWorkerRf3Protocol.InitialDeliveryGeneration), QueueWorkerRf3Protocol.Payload,
                QueueWorkerRf3Protocol.Headers));
    }

    internal static async Task AckedAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerRf3Seed seed,
        string id, long sequence, CancellationToken token)
        => await EqualAsync(await ImageAsync(sdk, mcp, seed.Lane, id, token),
            new MessageInspection(new(id, MessageState.Acked, QueueWorkerRf3Protocol.ClaimAttempt,
                QueueWorkerRf3Protocol.AckStateVersion, sequence, null, null, LeaseVersion: QueueWorkerRf3Protocol.InitialLeaseVersion, DeliveryGeneration: QueueWorkerRf3Protocol.InitialDeliveryGeneration), null, null));

    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid id, QueueWorkerRf3Seed seed,
        MutationReceipt[] mutations)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.Position).IsGreaterThan((long)QueueWorkerRf3Protocol.PositivePositionBoundary);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Lane.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(seed.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(seed.Placement.PlacementEpoch);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await EqualAsync(receipt.Mutations.ToArray(), mutations);
    }
}
