using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Queries;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMovePageAdmission
{
    internal static void Require(PartitionMoveImagePage page, PartitionMoveSourceFenceRecord fence,
        DatabaseLimits limits, int maximumPageBytes)
    {
        if (page is null || page.Version != PartitionMoveProtocol.Version
            || page.MoveId != fence.MoveId || page.Partition != fence.Partition
            || page.Ordinal < PartitionMoveProtocol.EmptyCount || page.Records.IsDefaultOrEmpty
            || !PartitionRecordFamilies.All.Contains(page.Family, StringComparer.Ordinal)
            || PartitionMoveFamilyCapture.ControlOwned(page.Family))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
        if (page.Records.Length > limits.MaxBatchMutations || maximumPageBytes > limits.MaxBatchBytes
            || NativeSerialization.Measure(page) > maximumPageBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var prefix = KeySpace.Partition(page.Family, page.Partition);
        ReadOnlyMemory<byte> previous = default;
        foreach (var row in page.Records)
        {
            if (row.Key.IsEmpty || !row.Key.Span.StartsWith(prefix)
                || !previous.IsEmpty && row.Key.Span.SequenceCompareTo(previous.Span) <= PartitionMoveProtocol.EmptyCount)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
            previous = row.Key;
        }
        if (page.Digest != PartitionMoveImageDigest.Records(page.Records))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
    }
}
