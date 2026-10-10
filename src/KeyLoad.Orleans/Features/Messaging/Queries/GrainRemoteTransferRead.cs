using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class GrainRemoteTransferRead
{
    internal static RemoteQueueTransferPeerResult Execute(DatabaseEngine database, PrincipalRecord principal,
        DecodedGrainRequest request, IOptions<DatabaseLimits> limits, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!RemoteTransferRequestScope.Validate(request.Envelope)
            || request.Envelope.ReadKind != GrainReadKind.QueueTransferCoordination)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var proof = GrainNativePayload.Read<RemoteTransferNativeProof>(request.Payload);
        if (proof.TechnicalPrincipalId != principal.Id || proof.Call.RequestId != request.Envelope.RequestId
            || proof.Call.ExpiresAt != request.Envelope.ExpiresAt)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var work = new ReadExecutionBudget(limits, clock, token);
        work.ConstrainLifetime(request.Envelope.ExpiresAt);
        var admitted = AdmittedRemoteTransferCall.Admit(database, proof.OriginalEnvelope,
            proof.OriginalSignature, proof.Call, work);
        return database.ObserveRemoteTransferPeer(admitted, work);
    }
}
