using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjectionShutdown(string root, IOptions<DatabaseLimits> limitsOptions, Guid sourceNodeId, NativeTextProjectionState state, NativeTextProjectionPhysicalGate physicalGate, Action<NativeTextFaultStage>? faultObserver, IOptions<NativeTextExecutionOptions> executionOptions, NativeTextResourceOwnership? resources = null)
{
    private const int FailureEmptyCount = 0;
    private const int FailureSingleItemCount = 1;
    private const int FailureIndexEmptyCount = 0;

    private readonly DatabaseLimits limits = limitsOptions.Value;
    internal void Run(NativeTextGenerationSlot?[] slots)
    {
        const int SlotsFirstIndex = 0;

        var failures = new List<Exception>();
        var current = slots[SlotsFirstIndex];
        foreach (var slot in slots)
        {
            if (slot is null)
            {
                continue;
            }
            var failure = Observe(slot, ReferenceEquals(slot, current));
            state.CompleteShutdown(slot, failure);
            if (failure is not null)
            {
                failures.Add(failure);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private Exception? Observe(NativeTextGenerationSlot slot, bool keepFiles)
    {
        var generation = slot.Generation;
        if (generation is null)
        {
            return DeleteBuilding(slot);
        }
        var failures = new List<Exception>();
        var canDelete = true;
        if (generation.Published && generation.CurrentIndex is not null)
        {
            var beforeRefresh = failures.Count;
            ServerFailureObserver.Observe(() => NativeTextSettlement.Refresh(root, limits, generation,
                budget: null, faultObserver, physicalGate, executionOptions: executionOptions), failures);
            canDelete = failures.Count == beforeRefresh;
        }
        if (generation.CurrentIndex is not null)
        {
            ServerFailureObserver.Observe(() => physicalGate.Run(generation.DisposeIndex), failures);
        }
        if (!keepFiles && canDelete && generation.CurrentIndex is null)
        {
            ServerFailureObserver.Observe(() => physicalGate.Run(() => NativeTextFiles.DeleteBuildingGeneration(
                root, generation.Leaf, sourceNodeId, limits, executionOptions: executionOptions, resources: resources)), failures);
            if (failures.Count == FailureEmptyCount)
            { slot.SharedReservation?.CompleteAfterJoinedCleanup(); }
        }
        return Failure(failures);
    }

    private Exception? DeleteBuilding(NativeTextGenerationSlot slot)
    {
        if (slot.Leaf is null)
        {
            return null;
        }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => physicalGate.Run(() => NativeTextFiles.DeleteBuildingGeneration(
            root, slot.Leaf, sourceNodeId, limits, executionOptions: executionOptions, resources: resources)), failures);
        if (failures.Count == FailureEmptyCount)
        { slot.SharedReservation?.CompleteAfterJoinedCleanup(); }
        return Failure(failures);
    }

    private static Exception? Failure(List<Exception> failures)
        => failures.Count switch { FailureEmptyCount => null, FailureSingleItemCount => failures[FailureIndexEmptyCount], _ => new AggregateException(failures) };
}
