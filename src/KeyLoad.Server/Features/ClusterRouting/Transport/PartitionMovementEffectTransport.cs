using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Transports original phase and outcome bytes through the actual native grain service.</summary>
internal sealed class PartitionMovementEffectTransport(PartitionMovementTransportContext context, PartitionMovementTransportReplyVerifier replyVerifier)
{
    internal async Task<PartitionMovementAuthenticatedReply> ExchangeOutcomeAsync(int index,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementOutcomeTransportRequest query,
        byte[] body, bool local, CancellationToken cancellationToken)
    {
        var address = await context.ResolveAsync(receiver, index, readOnly: true, cancellationToken).ConfigureAwait(false);
        var signature = context.Sign(body, PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.Outcome);
        var response = await context.ExchangeAsync(address, receiver.Owner.VoterIds[index],
            PartitionMovementGrainServiceChannel.Outcome, query: true, body, signature,
            readOnly: true, cancellationToken).ConfigureAwait(false);
        var reply = replyVerifier.ReadReplyMacPurpose(response, local, PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.Outcome);
        await replyVerifier.VerifyReplyAsync(index, receiver, query.Original with
        { Envelope = query.Original.Envelope with { ExpiresAt = query.ExpiresAt, Nonce = query.Nonce } },
            reply.Value, address, PartitionMoveOriginalDispatchIdentity.Digest(query.Original.CommandId, query.Original.Envelope),
            cancellationToken).ConfigureAwait(false);
        return reply;
    }

    internal async Task<PartitionMovementAuthenticatedReply> ExchangeAsync(int index, RegisteredPhysicalOwnerV1 receiver,
        PartitionMovementTransportRequest original, byte[] body, bool local, CancellationToken cancellationToken)
    {
        var address = await context.ResolveAsync(receiver, index, readOnly: false, cancellationToken).ConfigureAwait(false);
        var signature = context.Sign(body, PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.Effect);
        cancellationToken.ThrowIfCancellationRequested();
        PartitionMovementGrainServiceReply response;
        try
        {
            response = await context.ExchangeAsync(address, receiver.Owner.VoterIds[index],
                PartitionMovementGrainServiceChannel.Phase, query: false, body, signature,
                readOnly: false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (PartitionMovementTransportContext.IsUnresolvedTransport(error))
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, PartitionMovementProtocol.Unavailable);
            unknown.Data[PartitionMovementTransportContext.OriginalFailureKey] = error;
            throw unknown;
        }
        var reply = replyVerifier.ReadReplyMacPurpose(response, local, PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.Effect);
        await replyVerifier.VerifyReplyAsync(index, receiver, original, reply.Value, address,
            PartitionMoveOriginalDispatchIdentity.Digest(original.CommandId, original.Envelope), cancellationToken).ConfigureAwait(false);
        return reply;
    }
}
