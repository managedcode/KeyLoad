using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

internal static class GrainBm25CorpusPublicationCommand
{
    internal static string Resolve(DecodedGrainRequest request)
    {
        var phase = DatabaseEngine.Payload<Bm25CorpusPublicationPhaseCommand>(Read(request));
        var partition = phase.Current.Partition;
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
        return partition.AtomicPartitionId;
    }

    internal static Task<Result<object>> SubmitAsync(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, PrincipalRecord principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(principal);
        cancellationToken.ThrowIfCancellationRequested();
        var operation = database.OwnCommand(Read(request));
        if (operation.UserId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return coordinator.SubmitVerifiedAsync(operation, cancellationToken);
    }

    private static Command<DatabaseCommand> Read(DecodedGrainRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Envelope);
        if (!Bm25RequestScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var operation = GrainPayload.Read<Command<DatabaseCommand>>(request.Payload);
        if (operation.Value!.Kind != OperationKind.Bm25CorpusPublicationPhase
            || operation.Value!.Kind != request.Envelope.CommandKind || operation.CommandId != request.Envelope.CommandId
            || operation.UserId != request.Envelope.PrincipalId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var phase = DatabaseEngine.Payload<Bm25CorpusPublicationPhaseCommand>(operation);
        if (phase.CommandId != operation.CommandId || phase.Current is null || phase.Current.Partition is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return operation;
    }
}
