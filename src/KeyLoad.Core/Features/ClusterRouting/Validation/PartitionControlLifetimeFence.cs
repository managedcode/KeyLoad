using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionControlLifetimeFence
{
    internal static void Require(IKeyValueView view, PartitionMovePhaseGrant grant, PartitionControlCommandRecord record)
    {
        var database = PartitionMoveGrantStorage.DatabaseKey(grant.Partition.TenantId, grant.Partition.DatabaseId);
        if (grant.Settlement is null)
        {
            Positive(view, database);
            Positive(view, PartitionMoveGrantStorage.PrincipalKey(grant.OperatorPrincipalId));
            Positive(view, PartitionMoveGrantStorage.MoveCountKey(grant.Partition, grant.MoveId));
        }
        else if (grant.Settlement.CommandId != grant.PhaseCommandId
            || grant.Settlement.AppliedPosition <= PartitionMoveProtocol.EmptyCount
            || grant.Settlement.ControlIntentDigest != grant.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(grant.Settlement.PhysicalOwner, grant.ReceiverOwner))
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        }
        if (record.Phase != PartitionControlCommandPhase.Finalized)
        {
            Positive(view, database);
            Positive(view, PartitionMoveGrantStorage.PrincipalKey(record.Identity.PrincipalId));
        }
    }

    private static void Positive(IKeyValueView view, byte[] key)
    {
        if (PartitionMoveGrantStorage.Outstanding(view, key) <= PartitionMoveProtocol.EmptyCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        }
    }
}
