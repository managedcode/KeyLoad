using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryLeafOwnerValidation
{
    private const long InitialGeneration = 0;
    private const string InvalidOwnerDetail = "Partition query leaves do not share one owner and policy epoch.";

    internal static void Validate(PartitionQueryLeafResultV1? result, PartitionRef partition,
        StoreIdentity owner, PhysicalShardRecord? physicalOwner)
    {
        if (physicalOwner is not null)
        {
            if (result is null || result.Partition != partition || result.NodeId == Guid.Empty
                || result.Incarnation != physicalOwner.Incarnation || result.ReadGeneration < InitialGeneration)
            { throw Errors.Fail(ErrorCode.OwnershipLost, InvalidOwnerDetail); }
            return;
        }
        if (result is null || result.Partition != partition || result.NodeId != owner.NodeId
            || result.Incarnation != owner.Incarnation || result.ReadGeneration != owner.ReadGeneration)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, InvalidOwnerDetail);
        }
    }

}
