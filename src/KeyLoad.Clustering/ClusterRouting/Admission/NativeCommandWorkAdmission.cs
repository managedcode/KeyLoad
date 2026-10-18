using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Reuses Core admission at the actual command owner without another physical catalog.</summary>
internal static class NativeCommandWorkAdmission
{
    internal static void Bind(DatabaseEngine database, DecodedGrainRequest request, PrincipalRecord principal,
        TimeProvider clock, NativeCapabilityWorkLifetime? work, Command<DatabaseCommand>? originalIntent = null)
    {
        if (work is null)
        {
            return; // Direct component execution has no hosted resource-lifetime binding.
        }
        var kind = request.Envelope.CommandKind
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        if (kind == OperationKind.DropTable || DatabaseEngine.IsResourceFreeCommandWork(kind))
        {
            work.Bind([]); // Dropping a resource cannot retain its closing lifetime.
            return;
        }
        var operation = originalIntent ?? (CarriesVerifiedOperation(request, kind)
            ? database.OwnCommand(GrainNativePayload.Read<Command<DatabaseCommand>>(request.Payload))
            : database.CreateCommand(kind, request.Envelope.CommandId, principal.Id, clock.GetUtcNow(), request.Payload));
        if (operation.Value!.Kind != kind || operation.CommandId != request.Envelope.CommandId || operation.UserId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        database.ValidateResourceEffectSubmission(operation, clock.GetUtcNow(), work);
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
        => kind is OperationKind.OnlineTextPublicationPhase or OperationKind.Bm25CorpusPublicationPhase;
}
