using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjectionLifecycle
{
    private readonly IOptions<NativeTextExecutionOptions> executionOptions;

    private readonly DatabaseLimits limits;
    private readonly IOptions<DatabaseLimits> limitsOptions;
    private readonly NativeTextProjectionState state;
    private readonly NativeTextProjectionPhysicalGate physicalGate;
    private readonly Action<NativeTextFaultStage>? faultObserver;
    private readonly NativeTextProjectionShutdown shutdown;
    private readonly NativeTextResourceOwnership? resources;

    internal NativeTextProjectionLifecycle(string root, IOptions<DatabaseLimits> limitsOptions, Guid sourceNodeId, NativeTextProjectionState state, NativeTextProjectionPhysicalGate physicalGate, Action<NativeTextFaultStage>? faultObserver, IOptions<NativeTextExecutionOptions> executionOptions, NativeTextResourceOwnership? resources = null)
    {
        this.executionOptions = executionOptions;
        this.resources = resources;
        Root = root;
        SourceNodeId = sourceNodeId;
        this.limitsOptions = limitsOptions;
        limits = limitsOptions.Value;
        this.state = state;
        this.physicalGate = physicalGate;
        this.faultObserver = faultObserver;
        shutdown = new(root, limitsOptions, sourceNodeId, state, physicalGate, faultObserver, executionOptions: executionOptions, resources: resources);
    }

    internal string Root { get; }
    internal Guid SourceNodeId { get; }

    internal NativeTextGeneration CreateGeneration(NativeTextGenerationSlot slot, TextProjectionScope scope,
        ReadExecutionBudget budget)
    {
        var leaf = NativeTextValidation.GenerationLeaf();
        state.RecordLeaf(slot, leaf);
        NativeTextGeneration? generation = null;
        try
        {
            slot.SharedReservation = resources?.ReserveGeneration(Root, leaf, budget);
            physicalGate.Run(() =>
            {
                state.CaptureSlots(out var first, out var second, out var third);
                NativeTextFiles.WriteOwner(Root, leaf, SourceNodeId, scope, limits, first: first, second: second, third: third, executionOptions: executionOptions, resources: resources);
                state.MarkOwnerCreated(slot);
            });
            faultObserver?.Invoke(NativeTextFaultStage.OwnerFlushed);
            budget.Check();
            var provider = new NativeTextFileStreamProvider(Root, leaf, SourceNodeId, executionOptions: executionOptions, resources: resources);
            generation = physicalGate.Run(() =>
            {
                var created = new NativeTextGeneration(Root, leaf, SourceNodeId, scope, limitsOptions, provider, executionOptions: executionOptions, resources: resources);
                state.AttachGeneration(slot, created);
                return created;
            });
            CheckPhysical(budget);
            return generation;
        }
        catch (Exception primary)
        {
            RemoveFailedBuild(slot, leaf, primary, generation);
            throw;
        }
    }

    internal NativeTextGeneration OpenGeneration(NativeTextGenerationSlot slot, ReadExecutionBudget budget)
    {
        var generation = slot.Generation ?? throw NativeTextErrors.Corrupt();
        CheckPhysical(budget);
        physicalGate.Run(() => generation.OpenIndex(budget));
        return generation;
    }

    internal void Publish(NativeTextGeneration generation, ReadExecutionBudget budget)
    {
        var files = physicalGate.Run(() => generation.CloseAndCapture(budget));
        NativeTextSettlement.ThrowDeferredBudgetFailure(generation);
        physicalGate.Run(() => NativeTextFiles.CheckGenerationBound(generation.Path, budget: budget, executionOptions: executionOptions));
        budget.Check();
        physicalGate.Run(() => NativeTextFiles.WritePendingManifest(Root, generation.Leaf, generation.Scope,
            generation.SealRecords(), files, limits, budget: budget, executionOptions: executionOptions, resources: resources));
        budget.Check();
        faultObserver?.Invoke(NativeTextFaultStage.NativeInventoryFlushed);
        budget.Check();
        physicalGate.Run(() => NativeTextFiles.PublishManifest(Root, generation.Leaf, resources));
        budget.Check();
        faultObserver?.Invoke(NativeTextFaultStage.ManifestPublished);
        CheckPhysical(budget);
        PublishVerified(generation);
    }

    internal void Retire(NativeTextGenerationSlot slot)
    {
        Exception? failure = null;
        physicalGate.Run(() =>
        {
            try
            {
                var generation = slot.Generation ?? throw NativeTextErrors.Corrupt();
                generation.DisposeIndex();
                NativeTextFiles.DeleteOwnedGeneration(Root, generation.Leaf, SourceNodeId, limits, executionOptions: executionOptions, resources: resources);
                slot.SharedReservation?.CompleteAfterJoinedCleanup();
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                state.CompleteRetirement(slot, failure);
            }
        });
    }

    internal void Shutdown(NativeTextGenerationSlot?[] slots) => shutdown.Run(slots);

    internal void CheckPhysical(ReadExecutionBudget? budget)
    {
        physicalGate.Run(() =>
        {
            state.CaptureSlots(out var first, out var second, out var third);
            NativeTextPhysicalBudget.Check(Root, first, second, third, budget, executionOptions: executionOptions);
        });
    }

    private void PublishVerified(NativeTextGeneration generation)
    {
        var plan = state.Publish(generation);
        try
        {
            if (plan.ReplaceCurrent)
            {
                DeleteForReplacement(plan.Previous!, generation);
                Activate(generation);
            }
            else
            {
                Activate(generation);
            }
        }
        catch (Exception)
        {
            if (plan.ReplaceCurrent)
            {
                state.CancelReplacement();
            }
            throw;
        }
    }

    private void Activate(NativeTextGeneration generation)
    {
        generation.MarkPublished();
        faultObserver?.Invoke(NativeTextFaultStage.GenerationActivated);
    }

    private void DeleteForReplacement(NativeTextGenerationSlot previous, NativeTextGeneration generation)
    {
        physicalGate.Run(() =>
        {
            try
            {
                var previousGeneration = previous.Generation ?? throw NativeTextErrors.Corrupt();
                previousGeneration.DisposeIndex();
                NativeTextFiles.DeleteOwnedGeneration(Root, previousGeneration.Leaf, SourceNodeId, limits, executionOptions: executionOptions, resources: resources);
                previous.SharedReservation?.CompleteAfterJoinedCleanup();
                state.CompleteReplacement(previous, generation);
            }
            catch (Exception)
            {
                state.CancelReplacement();
                throw;
            }
        });
    }

    private void RemoveFailedBuild(NativeTextGenerationSlot slot, string leaf, Exception primary,
        NativeTextGeneration? generation)
    {
        var failures = new List<Exception> { primary };
        if (generation?.CurrentIndex is not null)
        {
            ServerFailureObserver.Observe(() => physicalGate.Run(generation.DisposeIndex), failures);
        }
        if (generation?.CurrentIndex is null)
        {
            ServerFailureObserver.Observe(() => physicalGate.Run(() =>
            {
                NativeTextFiles.DeleteBuildingGeneration(Root, leaf, SourceNodeId, limits, executionOptions: executionOptions, resources: resources);
                slot.SharedReservation?.CompleteAfterJoinedCleanup();
                state.ClearFailedBuild(slot);
            }), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
