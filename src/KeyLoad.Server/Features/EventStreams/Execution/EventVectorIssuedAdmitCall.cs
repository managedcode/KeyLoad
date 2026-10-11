using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorIssuedAdmitCall
{
    private const long FirstNativeTerm = 1;

    internal static async Task<GrainOperationReply> ExecuteAsync(OrleansNode node,
        DecodedGrainRequest parent, PrincipalRecord principal, ReplicatedOperation issued, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateEventVectorPhase(requestId, issued,
            parent.Envelope.ExpiresAt);
        using var context = node.OpenRequestContext(principal, requestId, issued.Id, cancellationToken);
        var reply = await node.ExecuteAsync(requestId, signed, command: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        work.Check();
        if (reply.Error is not null)
        { return reply; }
        work.ChargeBytes(reply.Payload.Length);
        var value = GrainNativePayload.Read<GrainValue>(reply.Payload).Value;
        var original = value switch
        {
            EventVectorFirstAdmitAppendResult appended => RequireFresh(appended, issued, parent.Envelope.RequestId),
            EventVectorFirstIssuanceResult known => known,
            _ => throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid)
        };
        if (original.NativeCommandId != issued.Id || original.Receipt is null
            || original.Receipt.CommandId != issued.Id || original.Issuance is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid); }
        return reply;
    }

    private static EventVectorFirstIssuanceResult RequireFresh(EventVectorFirstAdmitAppendResult appended,
        ReplicatedOperation issued, Guid originalParentRequestId)
    {
        if (originalParentRequestId == Guid.Empty || appended.OriginalParentRequestId != originalParentRequestId
            || appended.OperationId != issued.Id || appended.AppendedTerm < FirstNativeTerm
            || appended.OriginalOutcome is null || appended.OriginalOutcome.Error is not null
            || appended.OriginalOutcome.NativeValue is not EventVectorFirstIssuanceResult original
            || original.Issuance is null || original.Receipt is null
            || original.Issuance.OriginalParentRequestId != originalParentRequestId
            || original.Receipt.Token.Position != appended.AppendedIndex)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid); }
        return original;
    }
}
