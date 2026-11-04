using KeyLoad.Core;
using KeyLoad.Core.Search;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

internal static class GrainOnlineTextPublicationCommand
{
    internal static async Task<Result<OnlineTextIndexMaintenanceResult>> SubmitAsync(DatabaseEngine database,
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
        _ = work.AdmitOriginal(() => coordinator.SubmitVerifiedAsync<OnlineTextIndexMaintenanceResult>(operation, cancellationToken));
        return await work.AwaitCallerAsync(cancellationToken);
    }

    private static Command<DatabaseCommand> Read(DecodedGrainRequest request)
    {
        if (!OnlineTextRequestScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var operation = (GrainPayload.Read<Command<DatabaseCommand>>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as ManagedCode.Communication.Commands.Command<KeyLoad.DatabaseCommand> at this closed operation boundary before executing typed code. */);
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
