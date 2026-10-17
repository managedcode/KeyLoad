using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3ByteImages
{
    internal static async Task RequireAsync(KeyLoadClient admin, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerRf3ByteSeed seed, Delivery first, bool completedRemaining, CancellationToken token)
    {
        foreach (var id in QueueWorkerRf3Protocol.Messages)
        {
            var sequence = Array.IndexOf(QueueWorkerRf3Protocol.Messages, id) + QueueWorkerRf3Protocol.ReadySequenceOffset;
            var leased = id == QueueWorkerRf3Protocol.First;
            var terminal = !leased && completedRemaining;
            var metadata = new MessageMetadata(id, leased ? MessageState.Leased : terminal ? MessageState.Acked : MessageState.Ready,
                leased || terminal ? QueueWorkerRf3Protocol.ClaimAttempt : QueueWorkerRf3Protocol.NoAttempts,
                leased ? QueueWorkerRf3Protocol.ClaimStateVersion : terminal ? QueueWorkerRf3Protocol.AckStateVersion : QueueWorkerRf3Protocol.ReadyStateVersion,
                sequence, null, null, leased ? seed.Native.Identity.Principal.Id : null,
                leased || terminal ? QueueWorkerRf3Protocol.InitialLeaseVersion : QueueWorkerRf3Protocol.NoLeaseVersion,
                leased ? first.LeaseUntil : null, QueueWorkerRf3Protocol.InitialDeliveryGeneration);
            var reference = new InspectMessageRequest(seed.Native.Lane, id);
            await QueueWorkerRf3Assertions.EqualAsync(await PublicReadAssertions.MessageAsync(await admin.InspectAsync(reference, token)),
                new MessageInspection(metadata, terminal ? null : seed.Payload, terminal ? null : QueueWorkerRf3Protocol.Headers));
            await QueueWorkerRf3Assertions.EqualAsync(await QueueWorkerRf3Assertions.ImageAsync(sdk, mcp, seed.Native.Lane, id, token),
                new MessageInspection(metadata, terminal ? null : seed.Projected, terminal ? null : QueueWorkerRf3Protocol.Headers));
        }
    }

    internal static async Task DeliveryAsync(QueueWorkerRf3ByteSeed seed, Delivery delivery, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await QueueWorkerRf3Claims.RequireAsync(seed.Native, delivery);
        await QueueWorkerRf3Assertions.EqualAsync(delivery, new Delivery(delivery.Id, seed.Projected,
            QueueWorkerRf3Protocol.Headers, delivery.Token, QueueWorkerRf3Protocol.InitialLeaseVersion,
            delivery.LeaseUntil, QueueWorkerRf3Protocol.ClaimAttempt, QueueWorkerRf3Protocol.InitialDeliveryGeneration));
    }
}
