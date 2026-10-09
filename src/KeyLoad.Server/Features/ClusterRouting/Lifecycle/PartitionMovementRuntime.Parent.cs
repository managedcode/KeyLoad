using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementRuntime
{
    public async Task<PartitionMoveResult> ExecuteAsync(GrainRequestEnvelope trusted, PartitionMoveRequest request,
        ReadExecutionBudget work, Func<PartitionMovePhase, ValueTask> progress, CancellationToken cancellationToken)
    {
        PartitionMoveResult? actual = null;
        await RunAsync(async token =>
        {
            work.Check();
            var database = parentDatabase
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
            var runtimeClock = parentClock
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
            var owner = parent
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
            var principal = GrainRequestAuthority.ReloadForRequest(database, trusted, runtimeClock);
            GrainRequestAuthority.RequireAdministrator(principal);
            if (trusted.CommandId != request.MoveId || request.MoveId == Guid.Empty)
            { throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest); }
            actual = await owner.ExecuteAsync(principal.Id, request, work, progress, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return actual ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
    }
}
