using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal string ResolvePartitionMovementGrantOperator(PartitionRef partition, Guid moveId, Guid grantId)
        => Store.Read(view =>
        {
            var grant = PartitionMoveGrantStorage.Read(view, partition, grantId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            if (grant.Partition != partition || grant.MoveId != moveId || grant.GrantId != grantId
                || !PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, grant.ControlOwner)
                || string.IsNullOrWhiteSpace(grant.OperatorPrincipalId))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            return grant.OperatorPrincipalId;
        });
}
