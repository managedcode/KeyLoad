using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal static class PartitionMoveCleanupFamilies
{
    internal static readonly string[] All = PartitionRecordFamilies.All
        .Where(value => value != PartitionRecordFamilies.BlobState && value != PartitionRecordFamilies.BlobHead)
        .Prepend(PartitionRecordFamilies.BlobState).Append(PartitionRecordFamilies.BlobHead)
        .Append(PartitionMoveProtocol.TargetPageSpace).ToArray();
}
