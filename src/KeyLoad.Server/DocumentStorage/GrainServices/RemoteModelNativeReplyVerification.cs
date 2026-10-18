using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeReplyVerification
{
    internal static async Task<RemoteModelNativeReply> VerifyAsync(RemoteModelNativeClient client,
        RegisteredPhysicalOwnerV1 destination, RemoteModelNativeRequest request, RemoteModelNativeReply received,
        long maximum, NodeOptions options, PartitionHost partition, PartitionMovementGrainServiceClient movement,
        SiloStorageIdentityProvider local, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.ExpiresAt <= clock.GetUtcNow() || received is null || !RemoteModelNativeIdentity.ReplyShape(received, maximum))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof);
        }
        var reply = received with
        {
            Payload = received.Payload.ToArray()
        };
        using var targetMac = RemoteDocumentMac.FromConfiguredSecret(RemoteModelNativeIdentity.Secret(options, partition, destination.Owner));
        if (reply.Nonce != request.Nonce || !targetMac.VerifyNativeReply(reply)
            || reply.Error is null && !targetMac.Verify(reply.Payload.Span, reply.Signature, reply: true))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof);
        }
        await client.RequireCurrentAsync(destination, request.DestinationVoter, request.DestinationSiloAddress, token).ConfigureAwait(false);
        await movement.RequireCallerAsync(request.CallerVoter, request.CallerSiloAddress, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (request.ExpiresAt <= clock.GetUtcNow() || local.Read().SiloAddress != request.CallerSiloAddress)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteModelNativeProtocol.Unavailable);
        }
        return reply;
    }
}
