using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerClusterByteImages
{
    internal static async Task RequireAsync(KeyLoadClient admin, KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerClusterByteSeed seed, Delivery first, bool completedRemaining, CancellationToken token)
    {
        foreach (var id in QueueWorkerClusterProtocol.Messages)
        {
            var sequence = Array.IndexOf(QueueWorkerClusterProtocol.Messages, id) + QueueWorkerClusterProtocol.ReadySequenceOffset;
            var leased = id == QueueWorkerClusterProtocol.First;
            var terminal = !leased && completedRemaining;
            var metadata = new MessageMetadata(id, leased ? MessageState.Leased : terminal ? MessageState.Acked : MessageState.Ready,
                leased || terminal ? QueueWorkerClusterProtocol.ClaimAttempt : QueueWorkerClusterProtocol.NoAttempts,
                leased ? QueueWorkerClusterProtocol.ClaimStateVersion : terminal ? QueueWorkerClusterProtocol.AckStateVersion : QueueWorkerClusterProtocol.ReadyStateVersion,
                sequence, null, null, leased ? seed.Execution.Identity.Principal.Id : null,
                leased || terminal ? QueueWorkerClusterProtocol.InitialLeaseVersion : QueueWorkerClusterProtocol.NoLeaseVersion,
                leased ? first.LeaseUntil : null, QueueWorkerClusterProtocol.InitialDeliveryGeneration);
            var reference = new InspectMessageRequest(seed.Execution.Lane, id);
            await QueueWorkerClusterAssertions.EqualAsync(await PublicReadAssertions.MessageAsync(await admin.InspectAsync(reference, token)),
                new MessageInspection(metadata, terminal ? null : seed.Payload, terminal ? null : QueueWorkerClusterProtocol.Headers));
            await QueueWorkerClusterAssertions.EqualAsync(await QueueWorkerClusterAssertions.ImageAsync(sdk, mcp, seed.Execution.Lane, id, token),
                new MessageInspection(metadata, terminal ? null : seed.Projected, terminal ? null : QueueWorkerClusterProtocol.Headers));
        }
    }

    internal static async Task DeliveryAsync(QueueWorkerClusterByteSeed seed, Delivery delivery, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await QueueWorkerClusterClaims.RequireAsync(seed.Execution, delivery);
        await QueueWorkerClusterAssertions.EqualAsync(delivery, new Delivery(delivery.Id, seed.Projected,
            QueueWorkerClusterProtocol.Headers, delivery.Token, QueueWorkerClusterProtocol.InitialLeaseVersion,
            delivery.LeaseUntil, QueueWorkerClusterProtocol.ClaimAttempt, QueueWorkerClusterProtocol.InitialDeliveryGeneration));
    }
}
