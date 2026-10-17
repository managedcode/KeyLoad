using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.Search;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25PortableMovementCapture
{
    internal static (PartitionMoveSourceFenceRecord Fence, PartitionMoveImagePage Page) Read(
        ControlledPartitionMovementNativeHost source, CancellationToken token)
    {
        var limits = source.Database.Limits;
        var work = new ReadExecutionBudget(source.Runtime.Core.DatabaseLimits, source.Clock, token);
        return source.Store.Read(view =>
        {
            var fence = PartitionMoveSourceFenceStorage.Read(work.CreateView(view),
                ControlledPartitionMovementCorpus.Partition, limits.MaxBatchBytes)
                ?? throw new InvalidOperationException("The authentic retained source fence is absent.");
            var pages = ImmutableArray.CreateBuilder<PartitionMoveImagePage>();
            long retained = 0;
            var family = PartitionMoveFamilyCapture.Capture(view, fence, Bm25CorpusProtocol.CurrentFamily,
                work, new(work, limits.MaxScanRecords), limits, limits.MaxBatchBytes,
                limits.MaxQueryReadBytes, pages, ref retained);
            if (family.RecordCount != 1 || pages.Count != 1 || retained <= 0)
            {
                throw new InvalidOperationException("The original BM25 source capture is not one complete native head.");
            }
            return (fence, pages[0]);
        });
    }

    internal static async Task<ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot>> SixAsync(
        ControlledPartitionMovementNativeHosts owners, CancellationToken token)
    {
        foreach (var node in owners.Nodes)
        {
            await node.Partition.Coordinator.ReadBarrierAsync(token);
        }
        return await owners.CaptureAsync(token);
    }
}
