using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementNativePhaseOperations
{
    internal static async Task<PartitionMovementGrainServiceReply> PhaseAsync(PartitionMovementNativeReceiver owner,
        PartitionMovementGrainServiceRequest packet, CancellationToken cancellationToken)
    {
        var request = await owner.Admission.VerifyAsync(packet.Payload, packet.Signature, cancellationToken).ConfigureAwait(false);
        return await PartitionMovementNativeLifetime.WithinExpiryAsync(owner, request.Envelope.ExpiresAt, async token =>
        {
            var terminal = await PartitionMovementNativeReplies.ExecuteAsync(() => owner.Receiver.ExecuteAsync(request, token)).ConfigureAwait(false);
            var reply = new PartitionMovementTransportReply(request.CommandId, request.Envelope.Nonce,
                owner.Receiver.LocalOwner(), owner.Receiver.Discovery(), terminal,
                PartitionMoveOriginalDispatchIdentity.Digest(request.CommandId, request.Envelope));
            return PartitionMovementNativeReplies.Encode(owner, reply, static (mac, bytes) => mac.Sign(bytes, reply: true));
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<PartitionMovementGrainServiceReply> OutcomeAsync(PartitionMovementNativeReceiver owner,
        PartitionMovementGrainServiceRequest packet, CancellationToken cancellationToken)
    {
        var request = await owner.Admission.VerifyOutcomeAsync(packet.Payload, packet.Signature, cancellationToken).ConfigureAwait(false);
        return await PartitionMovementNativeLifetime.WithinExpiryAsync(owner, request.ExpiresAt, async token =>
        {
            var terminal = await PartitionMovementNativeReplies.ExecuteAsync(() => owner.Receiver.ExecuteOutcomeAsync(request, token)).ConfigureAwait(false);
            var reply = new PartitionMovementTransportReply(request.Original.CommandId, request.Nonce,
                owner.Receiver.LocalOwner(), owner.Receiver.Discovery(), terminal,
                PartitionMoveOriginalDispatchIdentity.Digest(request.Original.CommandId, request.Original.Envelope));
            return PartitionMovementNativeReplies.Encode(owner, reply, static (mac, bytes) => mac.SignOutcome(bytes, reply: true));
        }, cancellationToken).ConfigureAwait(false);
    }
}
