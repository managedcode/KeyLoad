using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Queries;

internal static class PartitionMoveImageCapture
{
    internal static PartitionMoveImage Capture(IKeyValueView view, PartitionMoveSourceFenceRecord fence,
        ReadExecutionBudget work, DatabaseLimits limits, int maximumPageBytes, long maximumImageBytes, int maximumRecords,
        ImmutableArray<ResourceDefinition> resources)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(work);
        if (maximumRecords <= PartitionMoveProtocol.EmptyCount || maximumRecords > limits.MaxScanRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        PartitionMoveSourceFenceValidation.Require(fence, fence.Partition);
        var persisted = PartitionMoveSourceFenceStorage.Read(work.CreateView(view), fence.Partition, limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireFence(view, fence, persisted, work);
        var captureBudget = new PartitionMoveCaptureBudget(work, maximumRecords);
        var pages = ImmutableArray.CreateBuilder<PartitionMoveImagePage>();
        var families = ImmutableArray.CreateBuilder<PartitionMoveImageFamily>();
        long retained = PartitionMoveProtocol.EmptyCount;
        foreach (var family in PartitionRecordFamilies.All)
        {
            work.Check();
            var captured = PartitionMoveFamilyCapture.Capture(view, fence, family, work, captureBudget, limits,
                maximumPageBytes, maximumImageBytes, pages, ref retained);
            families.Add(captured);
        }
        work.Check();
        var frozen = families.ToImmutable();
        return new(PartitionMoveProtocol.Version, fence.MoveId, fence.Partition, fence.SourcePlacement,
            fence.SourceCut, frozen, pages.ToImmutable(), PartitionMoveImageDigest.Image(frozen, resources), resources);
    }

    private static void RequireFence(IKeyValueView view, PartitionMoveSourceFenceRecord expected,
        PartitionMoveSourceFenceRecord actual, ReadExecutionBudget work)
    {
        if (expected.MoveId != actual.MoveId || expected.SourceCut != actual.SourceCut
            || expected.ControlIntentDigest != actual.ControlIntentDigest
            || !PartitionMoveControlValidation.SameSource(expected.SourcePlacement, actual.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(expected.ControlOwner, actual.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(expected.DestinationOwner, actual.DestinationOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var applied = work.Read(view, KeySpace.AppliedBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (NativeSerialization.Deserialize<long>(applied) < actual.SourceCut)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
    }
}
