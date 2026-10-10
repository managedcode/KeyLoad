using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class GrainOnlineTextPublicationCommand
{
    internal static string Resolve(DecodedGrainRequest request)
    {
        var phase = NativeCommandPayload.Read<OnlineTextPublicationPhaseCommand>(Read(request));
        var partition = phase.Request.Consumer.Partition;
        ArgumentNullException.ThrowIfNull(partition);
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
        return partition.AtomicPartitionId;
    }

    internal static async Task<OperationResult> SubmitAsync(DatabaseEngine database,
        ICommitCoordinator coordinator, DecodedGrainRequest request, PrincipalRecord principal,
        INativeOnlineTextMaintenance? maintenance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = database.VerifyOperationAuthority(Read(request));
        if (operation.PrincipalId != principal.Id || !principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
        var phase = NativeCommandPayload.Read<OnlineTextPublicationPhaseCommand>(operation);
        if (maintenance is null || coordinator is not ClusterCoordinator owner)
        { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
        var work = maintenance.RequirePublicationWork(phase.SessionId, phase.Request, principal.Id);
        _ = work.AdmitOriginal(() => owner.AdmitOnlineTextPublication(operation, cancellationToken));
        return await work.AwaitCallerAsync(cancellationToken).ConfigureAwait(true);
    }

    private static ReplicatedOperation Read(DecodedGrainRequest request)
    {
        if (!OnlineTextRequestScope.Validate(request.Envelope))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var operation = GrainNativePayload.Read<ReplicatedOperation>(request.Payload);
        if (operation.Kind != OperationKind.OnlineTextPublicationPhase
            || operation.Id != request.Envelope.CommandId
            || operation.PrincipalId != request.Envelope.PrincipalId)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var phase = NativeCommandPayload.Read<OnlineTextPublicationPhaseCommand>(operation);
        if (phase.Request.CommandId != operation.Id || phase.Result.CommandId != operation.Id)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return operation;
    }
}
