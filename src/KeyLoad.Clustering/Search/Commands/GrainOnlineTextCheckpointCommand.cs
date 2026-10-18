using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class GrainOnlineTextCheckpointCommand
{
    internal static Task<OperationResult>? TrySubmit(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, PrincipalRecord principal, INativeOnlineTextMaintenance? maintenance,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (maintenance is null || request.Envelope.CommandKind != OperationKind.CommitProjectionBatch)
        {
            return null;
        }
        var checkpoint = GrainNativePayload.ReadCommand<CommitProjectionBatchRequest>(request.Payload);
        var work = maintenance.TryRequireCheckpointWork(checkpoint, principal.Id, request.Envelope.ExpiresAt);
        if (work is null)
        {
            return null;
        }
        if (request.Envelope.CommandId != checkpoint.CommandId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        var operation = database.CreateNativeOperation(OperationKind.CommitProjectionBatch, checkpoint.CommandId,
            principal.Id, clock.GetUtcNow(), request.Payload);
        _ = work.AdmitOriginal(() => coordinator.SubmitVerifiedAsync(operation, cancellationToken));
        return work.AwaitCallerAsync(cancellationToken);
    }
}
