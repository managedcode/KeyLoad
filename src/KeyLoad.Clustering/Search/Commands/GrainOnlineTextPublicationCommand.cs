using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

internal static class GrainOnlineTextPublicationCommand
{
    internal static string Resolve(DecodedGrainRequest request)
    {
        var phase = OperationProtocol.Payload<OnlineTextPublicationPhaseCommand>(Read(request));
        var partition = phase.Request.Consumer.Partition;
        ArgumentNullException.ThrowIfNull(partition);
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
        return partition.AtomicPartitionId;
    }

    internal static async Task<Result<object>> SubmitAsync(DatabaseEngine database,
        ICommitCoordinator coordinator, DecodedGrainRequest request, PrincipalRecord principal,
        IOnlineTextMaintenance? maintenance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = database.OwnCommand(Read(request));
        if (operation.UserId != principal.Id || !principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        var phase = OperationProtocol.Payload<OnlineTextPublicationPhaseCommand>(operation);
        if (maintenance is null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        var work = maintenance.RequirePublicationWork(phase.SessionId, phase.Request, principal.Id);
        _ = work.AdmitOriginal(() => coordinator.SubmitVerifiedAsync(operation, cancellationToken));
        return await work.AwaitCallerAsync(cancellationToken).ConfigureAwait(true);
    }

    private static Command<DatabaseCommand> Read(DecodedGrainRequest request)
    {
        if (!OnlineTextRequestScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var operation = GrainPayload.Read<Command<DatabaseCommand>>(request.Payload);
        if (operation.Value!.Kind != OperationKind.OnlineTextPublicationPhase
            || operation.CommandId != request.Envelope.CommandId
            || operation.UserId != request.Envelope.PrincipalId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var phase = OperationProtocol.Payload<OnlineTextPublicationPhaseCommand>(operation);
        if (phase.Request.CommandId != operation.CommandId || phase.Result.CommandId != operation.CommandId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return operation;
    }
}
