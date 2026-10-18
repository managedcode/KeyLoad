using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Reuses Core admission at the actual command owner without another physical catalog.</summary>
internal static class NativeCommandWorkAdmission
{
    internal static void Bind(DatabaseEngine database, DecodedGrainRequest request, PrincipalRecord principal,
        TimeProvider clock, NativeCapabilityWorkLifetime? work, ReplicatedOperation? originalIntent = null)
    {
        if (work is null)
        {
            return; // Direct lower execution supplies no hosted D4 proof.
        }
        var kind = request.Envelope.CommandKind
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        if (kind == OperationKind.DropTable || DatabaseEngine.IsResourceFreeCommandWork(kind))
        {
            work.Bind([]); // Verified retirement/control work must not join its own old lifetime.
            return;
        }
        var operation = originalIntent ?? (CarriesVerifiedOperation(request, kind)
            ? database.VerifyOperationAuthority(GrainNativePayload.Read<ReplicatedOperation>(request.Payload))
            : database.CreateNativeOperation(kind, request.Envelope.CommandId, principal.Id, clock.GetUtcNow(), request.Payload));
        if (operation.Kind != kind || operation.Id != request.Envelope.CommandId || operation.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        database.ValidateResourceEffectSubmission(operation, clock.GetUtcNow(), work);
    }

    internal static void BindObserved(DatabaseEngine database, DecodedGrainRequest request,
        PrincipalRecord principal, TimeProvider clock, NativeCapabilityWorkLifetime commandWork,
        ReplicatedOperation originalIntent, ReadExecutionBudget budget)
    {
        budget.Check();
        var kind = request.Envelope.CommandKind
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        if (originalIntent.Kind != kind || originalIntent.Id != request.Envelope.CommandId
            || originalIntent.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        if (kind == OperationKind.DropTable || DatabaseEngine.IsResourceFreeCommandWork(kind))
        {
            commandWork.Bind([]);
            return;
        }
        database.ValidateResourceEffectSubmission(originalIntent, clock.GetUtcNow(), commandWork, budget);
        budget.Check();
    }

    internal static NativeCommandWorkProofV1 Capture(DecodedGrainRequest request,
        NativeCapabilityWorkLifetime work, long policyEpoch)
    {
        if (policyEpoch < NativeCommandWorkProofV1.MinimumPolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
        return new(NativeCommandWorkProofV1.CurrentVersion, request.Envelope.RequestId, request.Envelope.CommandId,
            request.Envelope.PrincipalId!, request.Envelope.CommandKind!.Value,
            [.. work.OriginalProducer.Dependencies], policyEpoch);
    }

    private static bool CarriesVerifiedOperation(DecodedGrainRequest request, OperationKind kind)
        => kind == OperationKind.Batch && request.Envelope.Purpose == global::KeyLoad.Core.Features.Messaging.RemoteTransferPeerProtocol.GrainPurpose
            && RemoteTransferRequestScope.Validate(request.Envelope)
            || kind is OperationKind.RemoteGraphSource or OperationKind.RemoteGraphReceive or OperationKind.RemoteGraphComplete
            or OperationKind.EventFeedControl or OperationKind.EventFeedSourcePhase or OperationKind.OnlineTextPublicationPhase
            or OperationKind.Bm25CorpusPublicationPhase or OperationKind.ResourceRetirementPhase;
}
