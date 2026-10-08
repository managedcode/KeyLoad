using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMoveDescriptorValidation
{
    internal static void Require(PartitionMoveImageDescriptor descriptor,
        PartitionMoveSourceFenceRecord fence, DatabaseLimits limits)
    {
        if (descriptor is null || descriptor.Version != PartitionMoveProtocol.Version
            || descriptor.MoveId != fence.MoveId || descriptor.Partition != fence.Partition
            || descriptor.SourceCut != fence.SourceCut || descriptor.Families.IsDefault
            || descriptor.Resources.IsDefault || descriptor.Resources.Length > limits.MaxBatchMutations
            || !PartitionMoveControlValidation.SameSource(descriptor.SourcePlacement, fence.SourcePlacement)
            || descriptor.Families.Any(family => family is null)
            || descriptor.Resources.Any(resource => resource is null)
            || !descriptor.Families.Select(family => family.Family)
                .SequenceEqual(PartitionRecordFamilies.All, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
        long records = PartitionMoveProtocol.EmptyCount;
        long bytes = PartitionMoveProtocol.EmptyCount;
        foreach (var family in descriptor.Families)
        {
            if (family.RecordCount < PartitionMoveProtocol.EmptyCount
                || family.RawBytes < PartitionMoveProtocol.EmptyCount
                || family.PageCount < PartitionMoveProtocol.EmptyCount
                || !PartitionMoveSourceFenceValidation.ValidDigest(family.Digest))
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
            if (family.RecordCount > limits.MaxScanRecords - records
                || family.RawBytes > limits.MaxQueryReadBytes - bytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
            records += family.RecordCount;
            bytes += family.RawBytes;
        }
        if (descriptor.Digest != PartitionMoveImageDigest.Image(descriptor.Families, descriptor.Resources))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
        if (NativeSerialization.Measure(descriptor) > limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
    }
}
