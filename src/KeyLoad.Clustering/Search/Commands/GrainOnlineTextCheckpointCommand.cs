using KeyLoad.Core;
using ManagedCode.Communication;

namespace KeyLoad.Clustering;

internal static class GrainOnlineTextCheckpointCommand
{
    internal static Task<Result<object>>? TrySubmit(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, PrincipalRecord principal, IOnlineTextMaintenance? maintenance,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (maintenance is null || request.Envelope.CommandKind != OperationKind.CommitProjectionBatch)
        {
            return null;
        }
        var checkpoint = GrainPayload.ReadCommand<CommitProjectionBatchRequest>(request.Payload);
        var work = maintenance.TryRequireCheckpointWork(checkpoint, principal.Id, request.Envelope.ExpiresAt);
        if (work is null)
        {
            if (request.Envelope.Purpose == OnlineTextRequestScope.Purpose)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            }
            return null;
        }
        if (request.Envelope.CommandId != checkpoint.CommandId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        var operation = database.CreateCommand(OperationKind.CommitProjectionBatch, checkpoint.CommandId,
            principal.Id, clock.GetUtcNow(), request.Payload);
        _ = work.AdmitOriginal(() => coordinator.SubmitVerifiedAsync(operation, cancellationToken));
        return work.AwaitCallerAsync(cancellationToken);
    }
}
