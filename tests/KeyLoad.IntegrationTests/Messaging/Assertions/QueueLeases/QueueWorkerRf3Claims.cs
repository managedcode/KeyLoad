using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3Claims
{
    internal static Task RequireAsync(QueueWorkerRf3Seed seed, Delivery delivery)
        => RequireAsync(seed.SigningKey, seed.Placement.Incarnation, seed.Lane, delivery.Id,
            seed.Identity.Principal.Id, delivery);

    internal static async Task RequireAsync(byte[] signingKey, Guid incarnation, QueueLaneRef lane,
        string messageId, string principalId, Delivery delivery)
    {
        var expected = new DeliveryClaims(lane, messageId, principalId,
            QueueWorkerRf3Protocol.InitialLeaseVersion, QueueWorkerRf3Protocol.InitialDeliveryGeneration, incarnation);
        await QueueWorkerRf3Assertions.EqualAsync(CoreNativeClaims.Verify<DeliveryClaims>(signingKey,
            delivery.Token, new NativeClaimsExecutionOptions().MaximumTokenCharacters), expected);
        await Assert.That(delivery.Token).IsEqualTo(CoreNativeClaims.Sign(signingKey, expected));
    }

    internal static async Task LiteralAsync(QueueWorkerRf3Seed seed, Delivery delivery, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Assert.That(QueueWorkerRf3Protocol.Messages).Contains(delivery.Id);
        await RequireAsync(seed, delivery);
        await QueueWorkerRf3Assertions.EqualAsync(delivery, new Delivery(delivery.Id,
            QueueWorkerRf3Protocol.Payload, QueueWorkerRf3Protocol.Headers, delivery.Token,
            QueueWorkerRf3Protocol.InitialLeaseVersion, delivery.LeaseUntil, QueueWorkerRf3Protocol.ClaimAttempt,
            QueueWorkerRf3Protocol.InitialDeliveryGeneration));
    }
}
