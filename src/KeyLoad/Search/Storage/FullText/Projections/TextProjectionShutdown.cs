using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal sealed class TextProjectionShutdown(string root, IOptions<DatabaseLimits> limitsOptions, Guid sourceNodeId, TextProjectionState state, TextProjectionPhysicalGate physicalGate, Action<TextFaultStage>? faultObserver, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
{
    private readonly DatabaseLimits limits = limitsOptions.Value;
    internal void Run(TextGenerationSlot?[] slots)
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

    private Exception? Observe(TextGenerationSlot slot, bool keepFiles)
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
            ServerFailureObserver.Observe(() => TextSettlement.Refresh(root, limits, generation,
                budget: null, faultObserver, physicalGate, executionOptions: executionOptions), failures);
            canDelete = failures.Count == beforeRefresh;
        }
        if (generation.CurrentIndex is not null)
        {
            ServerFailureObserver.Observe(() => physicalGate.Run(generation.DisposeIndex), failures);
        }
        if (!keepFiles && canDelete && generation.CurrentIndex is null)
        {
            ServerFailureObserver.Observe(() => physicalGate.Run(() => TextFiles.DeleteBuildingGeneration(
                root, generation.Leaf, sourceNodeId, limits, executionOptions: executionOptions, resources: resources)), failures);
            if (failures.Count == 0)
            {
                slot.SharedReservation?.CompleteAfterJoinedCleanup();
            }
        }
        return Failure(failures);
    }

    private Exception? DeleteBuilding(TextGenerationSlot slot)
    {
        if (slot.Leaf is null)
        {
            return null;
        }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => physicalGate.Run(() => TextFiles.DeleteBuildingGeneration(
            root, slot.Leaf, sourceNodeId, limits, executionOptions: executionOptions, resources: resources)), failures);
        if (failures.Count == 0)
        {
            slot.SharedReservation?.CompleteAfterJoinedCleanup();
        }
        return Failure(failures);
    }

    private static Exception? Failure(List<Exception> failures)
        => failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(failures)
        };
}
