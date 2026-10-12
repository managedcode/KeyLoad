namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Preserves original issuance/cancellation packets and authenticates the fresh native sender separately.</summary>
internal sealed class PartitionMovementReceiverIssueTransport(PartitionMovementTransportContext context, PartitionMovementTransportReplyVerifier replyVerifier)
{
    internal Task<PartitionMovementAuthenticatedReply> ExchangeReceiverIssueAsync(int index,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementReceiverExchange exchange,
        byte[] body, bool local, CancellationToken cancellationToken)
        => ExchangeAsync(index, receiver, exchange, body, local,
            PartitionMovementGrainServiceChannel.ReceiverIssue,
            PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.ReceiverIssue, cancellationToken);

    internal Task<PartitionMovementAuthenticatedReply> ExchangeRetireCancellationAsync(int index,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementReceiverExchange exchange,
        byte[] body, bool local, CancellationToken cancellationToken)
        => ExchangeAsync(index, receiver, exchange, body, local,
            PartitionMovementGrainServiceChannel.RetireCancellation,
            PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.RetireCancellation, cancellationToken);

    private async Task<PartitionMovementAuthenticatedReply> ExchangeAsync(int index,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementReceiverExchange exchange,
        byte[] body, bool local, PartitionMovementGrainServiceChannel channel,
        PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose purpose, CancellationToken token)
    {
        var address = await context.ResolveAsync(receiver, index, exchange.Query, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        PartitionMovementGrainServiceReply response;
        try
        {
            response = await context.ExchangeAsync(address, receiver.Owner.VoterIds[index], channel, exchange.Query,
                body, exchange.RequestSignature, exchange.Query, token).ConfigureAwait(false);
        }
        catch (Exception error) when (!exchange.Query && PartitionMovementTransportContext.IsUnresolvedTransport(error))
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, PartitionMovementProtocol.Unavailable);
            unknown.Data[PartitionMovementTransportContext.OriginalFailureKey] = error;
            throw unknown;
        }
        var reply = replyVerifier.ReadReplyMacPurpose(response, local, purpose);
        await replyVerifier.VerifyReplyAsync(index, receiver, exchange.ReplyScope, reply.Value, address,
            exchange.OriginalPhaseIdentityDigest, token).ConfigureAwait(false);
        return reply;
    }
}
