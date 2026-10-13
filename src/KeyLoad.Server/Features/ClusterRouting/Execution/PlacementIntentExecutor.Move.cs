using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PlacementIntentExecutor
{
    private const int CompletedMoveIncrement = 1;
    private const int FirstCapacityIndex = 0;
    private async Task<PlacementIntentV1> ApplyNextAsync(GrainRequestEnvelope trusted,
        PlacementIntentV1 intent, ReadExecutionBudget work, CancellationToken token)
    {
        var move = intent.Plan.Moves[intent.CompletedMoves];
        var principal = GrainRequestAuthority.ReloadForRequest(partition.Database, trusted, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var header = partition.Database.ReadPartitionMovementParentHeader(principal.Id, move, work);
        var receipt = header?.TerminalResult;
        if (receipt is null)
        {
            var fresh = await capacity.ReadAsync(principal, intent.Plan.OriginalPartitions,
                work, trusted.ExpiresAt, token).ConfigureAwait(false);
            partition.Database.ValidatePlacementIntentApply(principal.Id, intent, fresh,
                fresh[FirstCapacityIndex].CohortFingerprint, work, token);
            var originalMove = trusted with
            {
                CommandId = move.MoveId,
                CommandKind = OperationKind.MovePartition,
                Payload = NativeSerialization.Serialize(move)
            };
            receipt = await movement.ExecuteAsync(originalMove, move, work,
                static _ => ValueTask.CompletedTask, phaseObservation: null, token).ConfigureAwait(false);
        }
        if (receipt.Phase != PartitionMovePhase.Retired)
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, TenantPlacementPolicyProtocol.ScopeMismatch); }
        var status = intent.CompletedMoves + CompletedMoveIncrement == intent.Plan.Moves.Length
            ? PlacementIntentStatus.Completed : PlacementIntentStatus.Paused;
        var phase = new PlacementIntentPhaseRequestV1(TenantPlacementPolicyProtocol.Version,
            intent.Plan.TenantId, intent.Plan.DatabaseId, intent.IntentId, intent.Revision,
            intent.OriginalApplyCommandId!.Value, status, intent.Claim!, receipt, false, intent.Plan.ControlPartition, intent.OriginalApplyRequest!);
        return await phases.ExecuteAsync(principal.Id, phase, trusted.ExpiresAt, work, token).ConfigureAwait(false);
    }
}
