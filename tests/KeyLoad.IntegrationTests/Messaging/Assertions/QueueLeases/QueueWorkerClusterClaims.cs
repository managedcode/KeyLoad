using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerClusterClaims
{
    internal static Task RequireAsync(QueueWorkerClusterSeed seed, Delivery delivery)
        => RequireAsync(seed.SigningKey, seed.Receipt.Token.Incarnation, seed.Lane, delivery.Id,
            seed.Identity.Principal.Id, delivery);

    internal static async Task RequireAsync(byte[] signingKey, Guid incarnation, QueueLaneRef lane,
        string messageId, string principalId, Delivery delivery)
    {
        var expected = new DeliveryClaims(lane, messageId, principalId,
            QueueWorkerClusterProtocol.InitialLeaseVersion, QueueWorkerClusterProtocol.InitialDeliveryGeneration, incarnation);
        await QueueWorkerClusterAssertions.EqualAsync(CoreNativeClaims.Verify<DeliveryClaims>(signingKey,
            delivery.Token, new NativeClaimsExecutionOptions().MaximumTokenCharacters), expected);
        await Assert.That(delivery.Token).IsEqualTo(CoreNativeClaims.Sign(signingKey, expected));
    }

    internal static async Task LiteralAsync(QueueWorkerClusterSeed seed, Delivery delivery, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Assert.That(QueueWorkerClusterProtocol.Messages).Contains(delivery.Id);
        await RequireAsync(seed, delivery);
        await QueueWorkerClusterAssertions.EqualAsync(delivery, new Delivery(delivery.Id,
            QueueWorkerClusterProtocol.Payload, QueueWorkerClusterProtocol.Headers, delivery.Token,
            QueueWorkerClusterProtocol.InitialLeaseVersion, delivery.LeaseUntil, QueueWorkerClusterProtocol.ClaimAttempt,
            QueueWorkerClusterProtocol.InitialDeliveryGeneration));
    }
}
