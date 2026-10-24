using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueWorkerClusterAssertions
{
    internal static Task EqualAsync<T>(T actual, T expected) => QueueLeaseClusterAssertions.EqualAsync(actual, expected);

    internal static async Task<MessageInspection?> ImageAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueLaneRef lane, string id, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, id);
        var image = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(request, token));
        await EqualAsync((await PublicReadAssertions.MessageAsync(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value, image);
        return image;
    }

    internal static async Task ReadyAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        string id, long sequence, CancellationToken token)
        => await EqualAsync(await ImageAsync(sdk, mcp, seed.Lane, id, token),
            new MessageInspection(new(id, MessageState.Ready, QueueWorkerClusterProtocol.NoAttempts,
                QueueWorkerClusterProtocol.ReadyStateVersion, sequence, null, null, DeliveryGeneration: QueueWorkerClusterProtocol.InitialDeliveryGeneration), QueueWorkerClusterProtocol.Payload,
                QueueWorkerClusterProtocol.Headers));

    internal static async Task LeasedAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        QueueWorkerDeliveryContext context, long sequence, CancellationToken token)
    {
        var delivery = context.Delivery;
        await QueueWorkerClusterClaims.RequireAsync(seed, delivery);
        await EqualAsync(delivery, new Delivery(delivery.Id, QueueWorkerClusterProtocol.Payload,
            QueueWorkerClusterProtocol.Headers, delivery.Token, QueueWorkerClusterProtocol.InitialLeaseVersion,
            delivery.LeaseUntil, QueueWorkerClusterProtocol.ClaimAttempt, QueueWorkerClusterProtocol.InitialDeliveryGeneration));
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        await EqualAsync(await ImageAsync(sdk, mcp, seed.Lane, delivery.Id, token),
            new MessageInspection(new(delivery.Id, MessageState.Leased, QueueWorkerClusterProtocol.ClaimAttempt,
                QueueWorkerClusterProtocol.ClaimStateVersion, sequence, null, null, seed.Identity.Principal.Id,
                QueueWorkerClusterProtocol.InitialLeaseVersion, delivery.LeaseUntil, DeliveryGeneration: QueueWorkerClusterProtocol.InitialDeliveryGeneration), QueueWorkerClusterProtocol.Payload,
                QueueWorkerClusterProtocol.Headers));
    }

    internal static async Task AckedAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueWorkerClusterSeed seed,
        string id, long sequence, CancellationToken token)
        => await EqualAsync(await ImageAsync(sdk, mcp, seed.Lane, id, token),
            new MessageInspection(new(id, MessageState.Acked, QueueWorkerClusterProtocol.ClaimAttempt,
                QueueWorkerClusterProtocol.AckStateVersion, sequence, null, null, LeaseVersion: QueueWorkerClusterProtocol.InitialLeaseVersion, DeliveryGeneration: QueueWorkerClusterProtocol.InitialDeliveryGeneration), null, null));

    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid id, QueueWorkerClusterSeed seed,
        MutationReceipt[] mutations)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.Position).IsGreaterThan((long)QueueWorkerClusterProtocol.PositivePositionBoundary);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Lane.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await EqualAsync(receipt.Mutations.ToArray(), mutations);
    }
}
