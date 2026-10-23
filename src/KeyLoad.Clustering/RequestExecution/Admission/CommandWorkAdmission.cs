using KeyLoad.Core;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

/// <summary>Reuses Core admission at the actual command owner without another physical catalog.</summary>
internal static class CommandWorkAdmission
{
    internal static Command<DatabaseCommand>? Bind(DatabaseEngine database, DecodedGrainRequest request, PrincipalRecord principal,
        TimeProvider clock, CapabilityWorkLifetime? work, ReadExecutionBudget budget, Command<DatabaseCommand>? originalIntent = null)
    {
        if (work is null)
        {
            return null; // Direct component execution has no hosted resource-lifetime binding.
        }
        var kind = request.Envelope.CommandKind
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        if (kind == OperationKind.DropTable || CommandResultBinding.IsResourceFreeCommandWork(kind))
        {
            work.Bind([]); // Dropping a resource cannot retain its closing lifetime.
            return null;
        }
        var operation = originalIntent ?? (CarriesVerifiedOperation(kind)
            ? database.OwnCommand(GrainPayload.Read<Command<DatabaseCommand>>(request.Payload))
            : database.CreateCommand(kind, request.Envelope.CommandId, principal.Id, clock.GetUtcNow(), request.Payload, budget));
        if (operation.Value!.Kind != kind || operation.CommandId != request.Envelope.CommandId || operation.UserId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        database.ValidateResourceEffectSubmission(operation, clock.GetUtcNow(), work, budget);
        return operation;
    }

    internal static CommandWorkProof Capture(DecodedGrainRequest request,
        CapabilityWorkLifetime work, long policyEpoch)
    {
        if (policyEpoch < CommandWorkProof.MinimumPolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
        return new(CommandWorkProof.CurrentVersion, request.Envelope.RequestId, request.Envelope.CommandId,
            request.Envelope.PrincipalId!, request.Envelope.CommandKind!.Value,
            [.. work.OriginalProducer.Dependencies], policyEpoch);
    }

    private static bool CarriesVerifiedOperation(OperationKind kind)
        => kind is OperationKind.OnlineTextPublicationPhase or OperationKind.Bm25CorpusPublicationPhase;
}
