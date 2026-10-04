namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjectionShutdown(string root, DatabaseLimits limits, Guid sourceNodeId,
    NativeTextProjectionState state, NativeTextProjectionPhysicalGate physicalGate,
    Action<NativeTextFaultStage>? faultObserver)
{
    internal void Run(NativeTextGenerationSlot?[] slots)
    {
        var failures = new List<Exception>();
        var current = slots[0];
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
                budget: null, faultObserver, physicalGate), failures);
            canDelete = failures.Count == beforeRefresh;
        }
        if (generation.CurrentIndex is not null)
        {
            ServerFailureObserver.Observe(() => physicalGate.Run(generation.DisposeIndex), failures);
        }
        if (!keepFiles && canDelete && generation.CurrentIndex is null)
        {
            ServerFailureObserver.Observe(() => physicalGate.Run(() => NativeTextFiles.DeleteBuildingGeneration(
                root, generation.Leaf, sourceNodeId, limits)), failures);
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
            root, slot.Leaf, sourceNodeId, limits)), failures);
        return Failure(failures);
    }

    private static Exception? Failure(List<Exception> failures)
        => failures.Count switch { 0 => null, 1 => failures[0], _ => new AggregateException(failures) };
}
