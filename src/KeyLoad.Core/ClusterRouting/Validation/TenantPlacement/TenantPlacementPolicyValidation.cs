using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class TenantPlacementPolicyValidation
{
    private const long MinimumBytes = 0;
    private const int MinimumBasisPoints = 0;
    private const long MinimumRevision = 1;
    private const int ConfiguredPhysicalOwnerCount = 2;
    private const int MinimumOwnerWeight = 1;
    private const int MaximumOwnerWeight = 16;

    internal static void Require(TenantPlacementPolicyV1 value, ErrorCode error)
    {
        ArgumentNullException.ThrowIfNull(value);
        JsonData.Identifier(value.TenantId);
        JsonData.Identifier(value.DatabaseId);
        if (value.Version != TenantPlacementPolicyProtocol.Version || value.Revision < MinimumRevision
            || value.PromotionBytes <= MinimumBytes || value.DemotionBytes < MinimumBytes
            || value.DemotionBytes >= value.PromotionBytes || value.MinimumFreeBytes < MinimumBytes
            || value.MinimumFreeBasisPoints is < MinimumBasisPoints or > TenantPlacementPolicyProtocol.BasisPoints
            || value.MaximumMemoryBasisPoints is <= MinimumBasisPoints or > TenantPlacementPolicyProtocol.BasisPoints
            || value.MaximumCpuBasisPoints is <= MinimumBasisPoints or > TenantPlacementPolicyProtocol.BasisPoints
            || value.OwnerWeights.IsDefaultOrEmpty || value.OwnerWeights.Length > ConfiguredPhysicalOwnerCount
            || value.OwnerWeights.Any(item => item.PhysicalShardId == Guid.Empty || item.Weight is < MinimumOwnerWeight or > MaximumOwnerWeight)
            || value.OwnerWeights.Select(item => item.PhysicalShardId).Distinct().Count() != value.OwnerWeights.Length)
        {
            throw Errors.Fail(error, TenantPlacementPolicyProtocol.Invalid);
        }
    }

    internal static void RequirePlan(TenantPlacementPlanV1 value, ErrorCode error)
    {
        ArgumentNullException.ThrowIfNull(value);
        JsonData.Identifier(value.TenantId);
        JsonData.Identifier(value.DatabaseId);
        if (value.Version != TenantPlacementPolicyProtocol.Version || value.PlanId == Guid.Empty
            || value.PolicyRevision < MinimumRevision || value.DirectoryRevision < MinimumBytes
            || string.IsNullOrWhiteSpace(value.CohortFingerprint) || value.SampledAt.Offset != TimeSpan.Zero
            || value.ExpiresAt.Offset != TimeSpan.Zero || value.ExpiresAt <= value.SampledAt
            || value.OriginalPartitions.IsDefaultOrEmpty || value.OriginalPartitions.Length > TenantPlacementPolicyProtocol.MaximumMoves
            || value.OriginalPartitions.Distinct().Count() != value.OriginalPartitions.Length
            || value.OriginalPartitions.Contains(value.ControlPartition)
            || value.OriginalPartitions.Any(part => part.TenantId != value.TenantId || part.DatabaseId != value.DatabaseId)
            || value.Moves.IsDefault || value.Moves.Length > TenantPlacementPolicyProtocol.MaximumMoves
            || value.CapacityDigests.IsDefaultOrEmpty || value.CapacityDigests.Any(string.IsNullOrWhiteSpace)
            || value.Moves.Select(move => move.Partition).Distinct().Count() != value.Moves.Length
            || value.Moves.Select(move => move.MoveId).Distinct().Count() != value.Moves.Length)
        {
            throw Errors.Fail(error, TenantPlacementPolicyProtocol.Invalid);
        }
        foreach (var move in value.Moves)
        {
            DatabaseEngine.ValidatePartition(move.Partition);
            if (!value.OriginalPartitions.Contains(move.Partition) || move.Partition.TenantId != value.TenantId || move.Partition.DatabaseId != value.DatabaseId
                || move.MoveId == Guid.Empty || move.Mode != PartitionMoveMode.Transfer
                || move.DestinationPhysicalShardId == Guid.Empty
                || move.ExpectedPlacementRevision < MinimumBytes)
            {
                throw Errors.Fail(error, TenantPlacementPolicyProtocol.Invalid);
            }
        }
        _ = checked(value.DirectoryRevision + value.Moves.Length * TenantPlacementPolicyProtocol.RevisionStep);
    }
}
