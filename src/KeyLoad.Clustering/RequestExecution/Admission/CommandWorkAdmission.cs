using KeyLoad.Core;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

/// <summary>Reuses Core admission at the actual command owner without another physical catalog.</summary>
internal static class CommandWorkAdmission
{
    internal static (bool HasValue, Command<DatabaseCommand>? Value) Bind(DatabaseEngine database, DecodedGrainRequest request, PrincipalRecord principal,
        TimeProvider clock, CapabilityWorkLifetime? work, ReadExecutionBudget budget, Command<DatabaseCommand>? originalIntent = null)
    {
        if (work is null)
        {
            return (false, default(Command<DatabaseCommand>?)); // Direct component execution has no hosted resource-lifetime binding.
        }
        var kind = request.Envelope.CommandKind
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        if (kind == OperationKind.DropTable || CommandResultBinding.IsResourceFreeCommandWork(kind))
        {
            work.Bind([]); // Dropping a resource cannot retain its closing lifetime.
            return (false, default(Command<DatabaseCommand>?));
        }
        var operation = originalIntent ?? (CarriesVerifiedOperation(kind)
            ? database.OwnCommand((GrainPayload.Read<Command<DatabaseCommand>>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as ManagedCode.Communication.Commands.Command<KeyLoad.DatabaseCommand> at this closed operation boundary before executing typed code. */))
            : (database.CreateCommand(kind, request.Envelope.CommandId, principal.Id, clock.GetUtcNow(), (GrainPayload.ReadCommandInput(kind, (request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as the selected canonical command input at this closed operation boundary before executing typed code. */), budget) /* KLD0041: Create the current canonical DatabaseCommand from its validated closed-kind native input before binding the original resource lifetime. */));
        if (operation.Value!.Kind != kind || operation.CommandId != request.Envelope.CommandId || operation.UserId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        database.ValidateResourceEffectSubmission(operation, clock.GetUtcNow(), work, budget);
        return (true, ((Command<DatabaseCommand>?)(operation)));
    }

    internal static CommandWorkProof Capture(DecodedGrainRequest request,
        CapabilityWorkLifetime work, long policyEpoch)
    {
        if (policyEpoch < CommandWorkProof.MinimumPolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
        return new(request.Envelope.RequestId, request.Envelope.CommandId,
            request.Envelope.PrincipalId!, request.Envelope.CommandKind!.Value,
            [.. work.OriginalProducer.Dependencies], policyEpoch);
    }

    private static bool CarriesVerifiedOperation(OperationKind kind)
        => kind is OperationKind.OnlineTextPublicationPhase or OperationKind.Bm25CorpusPublicationPhase;
}
