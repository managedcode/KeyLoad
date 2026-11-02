using KeyLoad.Core;
using KeyLoad.Core.Search;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

internal static class GrainBm25CorpusPublicationCommand
{
    internal static Task<Result<CommitReceipt>> SubmitAsync(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, PrincipalRecord principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database) /* KLD0041: BCL null validation accepts the database DatabaseEngine reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(coordinator) /* KLD0041: BCL null validation accepts the coordinator ICommitCoordinator reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(principal) /* KLD0041: BCL null validation accepts the principal PrincipalRecord reference through its object parameter. */;
        cancellationToken.ThrowIfCancellationRequested();
        var operation = database.OwnCommand(Read(request));
        if (operation.UserId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return coordinator.SubmitVerifiedAsync<CommitReceipt>(operation, cancellationToken);
    }

    private static Command<DatabaseCommand> Read(DecodedGrainRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Envelope) /* KLD0041: BCL null validation accepts the request.Envelope GrainRequestEnvelope reference through its object parameter. */;
        if (!Bm25RequestScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var operation = (GrainPayload.Read<Command<DatabaseCommand>>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as ManagedCode.Communication.Commands.Command<KeyLoad.DatabaseCommand> at this closed operation boundary before executing typed code. */);
        if (operation.Value!.Kind != OperationKind.Bm25CorpusPublicationPhase
            || operation.Value!.Kind != request.Envelope.CommandKind || operation.CommandId != request.Envelope.CommandId
            || operation.UserId != request.Envelope.PrincipalId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var phase = OperationProtocol.Payload<Bm25CorpusPublicationPhaseCommand>(operation);
        if (phase.CommandId != operation.CommandId || phase.Current is null || phase.Current.Partition is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return operation;
    }
}
