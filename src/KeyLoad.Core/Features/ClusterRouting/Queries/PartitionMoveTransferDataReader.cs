using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Queries;

/// <summary>Verifies current transfer bytes against an already authenticated original descriptor.</summary>
internal static class PartitionMoveTransferDataReader
{
    internal static PartitionMoveTransferDataRead Read(IKeyValueView view,
        PartitionMoveSourceFenceRecord originalFence, PartitionMoveImageDescriptor originalDescriptor,
        ReadExecutionBudget work, DatabaseLimits limits, int maximumPageBytes,
        long maximumImageBytes, int maximumRecords)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(work);
        if (maximumRecords <= PartitionMoveProtocol.EmptyCount || maximumRecords > limits.MaxScanRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        PartitionMoveSourceFenceValidation.Require(originalFence, originalFence.Partition);
        PartitionMoveDescriptorValidation.Require(originalDescriptor, originalFence, limits);
        var persisted = PartitionMoveSourceFenceStorage.Read(work.CreateView(view), originalFence.Partition,
            limits.MaxBatchBytes) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var currentReadCut = PartitionMoveImageCapture.RequireFence(view, originalFence, persisted, work);
        var captureBudget = new PartitionMoveCaptureBudget(work, maximumRecords);
        var pages = ImmutableArray.CreateBuilder<PartitionMoveImagePage>();
        long retained = PartitionMoveProtocol.EmptyCount;
        foreach (var expected in originalDescriptor.Families)
        {
            work.Check();
            if (PartitionMoveFamilyCapture.ControlOwned(expected.Family))
            { continue; }
            var actual = PartitionMoveFamilyCapture.Capture(view, originalFence, expected.Family,
                work, captureBudget, limits, maximumPageBytes, maximumImageBytes, pages, ref retained);
            if (actual != expected)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.InvalidImage); }
        }
        work.Check();
        return new(originalDescriptor, pages.ToImmutable(), currentReadCut);
    }
}

/// <summary>Local read result; CurrentReadCut never replaces the descriptor's original SourceCut.</summary>
internal sealed record PartitionMoveTransferDataRead(PartitionMoveImageDescriptor OriginalDescriptor,
    ImmutableArray<PartitionMoveImagePage> Pages, long CurrentReadCut);
