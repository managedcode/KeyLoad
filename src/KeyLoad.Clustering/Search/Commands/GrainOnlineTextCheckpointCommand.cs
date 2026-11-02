using KeyLoad.Core;
using ManagedCode.Communication;

namespace KeyLoad.Clustering;

internal static class GrainOnlineTextCheckpointCommand
{
    internal static (bool HasValue, Task<Result<ProjectionBatchResult>>? Value) TrySubmit(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, PrincipalRecord principal, IOnlineTextMaintenance? maintenance,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (maintenance is null || request.Envelope.CommandKind != OperationKind.CommitProjectionBatch)
        {
            return (false, default(Task<Result<ProjectionBatchResult>>?));
        }
        var checkpoint = (GrainPayload.ReadCommand<CommitProjectionBatchRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.CommitProjectionBatchRequest at this closed operation boundary before executing typed code. */);
        var work = maintenance.TryRequireCheckpointWork(checkpoint, principal.Id);
        if (work is null)
        {
            if (request.Envelope.Purpose == OnlineTextRequestScope.Purpose)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            }
            return (false, default(Task<Result<ProjectionBatchResult>>?));
        }
        if (request.Envelope.CommandId != checkpoint.CommandId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        var operation = (database.CreateCommand(OperationKind.CommitProjectionBatch, checkpoint.CommandId,
            principal.Id, clock.GetUtcNow(), checkpoint) /* KLD0041: Create the current canonical DatabaseCommand from the concrete projection checkpoint without changing its stored Input field representation. */);
        _ = work.AdmitOriginal(() => coordinator.SubmitVerifiedAsync<ProjectionBatchResult>(operation, cancellationToken));
        return (true, ((Task<Result<ProjectionBatchResult>>?)(work.AwaitCallerAsync(cancellationToken))));
    }
}
