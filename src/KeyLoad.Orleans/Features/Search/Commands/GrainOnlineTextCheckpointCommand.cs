using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class GrainOnlineTextCheckpointCommand
{
    internal static Task<OperationResult>? TrySubmit(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, PrincipalRecord principal, INativeOnlineTextMaintenance? maintenance,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (maintenance is null || request.Envelope.CommandKind != OperationKind.CommitProjectionBatch)
        { return null; }
        var checkpoint = GrainNativePayload.ReadCommand<CommitProjectionBatchRequest>(request.Payload);
        var work = maintenance.TryRequireCheckpointWork(checkpoint, principal.Id, request.Envelope.ExpiresAt);
        if (work is null)
        { return null; }
        if (coordinator is not ClusterCoordinator owner || request.Envelope.CommandId != checkpoint.CommandId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
        var operation = database.CreateNativeOperation(OperationKind.CommitProjectionBatch, checkpoint.CommandId,
            principal.Id, clock.GetUtcNow(), request.Payload);
        _ = work.AdmitOriginal(() => owner.AdmitOnlineProjectionCheckpoint(operation, cancellationToken));
        return work.AwaitCallerAsync(cancellationToken);
    }
}
