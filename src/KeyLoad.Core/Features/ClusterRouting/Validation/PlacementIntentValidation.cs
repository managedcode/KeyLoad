using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PlacementIntentValidation
{
    private const int NoCompletedMoves = 0;
    private const long MinimumRevision = 1;

    internal static void Require(PlacementIntentV1 value, ErrorCode error)
    {
        if (value.Version != TenantPlacementPolicyProtocol.Version || value.IntentId == Guid.Empty
            || string.IsNullOrWhiteSpace(value.PrincipalId) || value.PolicyEpoch < MinimumRevision
            || value.Revision < MinimumRevision || !Enum.IsDefined(value.Status)
            || value.Status == PlacementIntentStatus.None || value.CompletedMoves < NoCompletedMoves
            || value.CompletedMoves > value.Plan.Moves.Length || value.Receipts.IsDefault
            || value.Receipts.Length != value.CompletedMoves
            || value.Status == PlacementIntentStatus.Completed && value.CompletedMoves != value.Plan.Moves.Length
            || (value.OriginalApplyCommandId is null) != (value.OriginalApplyRequest is null)
            || value.OriginalApplyRequest is { } apply && (apply.IntentId != value.IntentId
                || apply.TenantId != value.Plan.TenantId || apply.DatabaseId != value.Plan.DatabaseId
                || apply.ControlPartition != value.Plan.ControlPartition || apply.ExpectedRevision < MinimumRevision)
            || value.Status == PlacementIntentStatus.Pending && value.OriginalApplyCommandId is not null
            || value.Status == PlacementIntentStatus.Applying && (value.OriginalApplyCommandId is null || value.Claim is null)
            || value.Claim is { } claim && (claim.Nonce == Guid.Empty || string.IsNullOrWhiteSpace(claim.VoterId)
                || string.IsNullOrWhiteSpace(claim.SiloAddress) || string.IsNullOrWhiteSpace(claim.CohortFingerprint)))
        { throw Errors.Fail(error, TenantPlacementPolicyProtocol.Malformed); }
        TenantPlacementPolicyValidation.RequirePlan(value.Plan, error);
        for (var index = NoCompletedMoves; index < value.Receipts.Length; index++)
        {
            var original = value.Plan.Moves[index];
            var receipt = value.Receipts[index];
            if (receipt.MoveId != original.MoveId || receipt.Partition != original.Partition
                || receipt.DestinationOwner.PhysicalShardId != original.DestinationPhysicalShardId
                || receipt.Phase != PartitionMovePhase.Retired || receipt.PublishedPlacement is null)
            { throw Errors.Fail(error, TenantPlacementPolicyProtocol.Malformed); }
        }
    }
}
