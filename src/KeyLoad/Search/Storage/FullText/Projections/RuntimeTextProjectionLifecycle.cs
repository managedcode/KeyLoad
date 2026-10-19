using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class RuntimeTextProjectionLifecycle
{
    private readonly IOptions<TextExecutionOptions> executionOptions;

    private readonly DatabaseLimits limits;
    private readonly IOptions<DatabaseLimits> limitsOptions;
    private readonly TextProjectionState state;
    private readonly TextProjectionPhysicalGate physicalGate;
    private readonly Action<TextFaultStage>? faultObserver;
    private readonly TextProjectionShutdown shutdown;
    private readonly TextResourceOwnership? resources;

    internal RuntimeTextProjectionLifecycle(string root, IOptions<DatabaseLimits> limitsOptions, Guid sourceNodeId, TextProjectionState state, TextProjectionPhysicalGate physicalGate, Action<TextFaultStage>? faultObserver, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
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

    internal string Root
    {
        get;
    }
    internal Guid SourceNodeId
    {
        get;
    }

    internal TextGeneration CreateGeneration(TextGenerationSlot slot, TextProjectionScope scope,
        ReadExecutionBudget budget)
    {
        var leaf = TextValidation.GenerationLeaf();
        state.RecordLeaf(slot, leaf);
        TextGeneration? generation = null;
        try
        {
            slot.SharedReservation = resources?.ReserveGeneration(Root, leaf, budget);
            physicalGate.Run(() =>
            {
                state.CaptureSlots(out var first, out var second, out var third);
                TextFiles.WriteOwner(Root, leaf, SourceNodeId, scope, limits, first: first, second: second, third: third, executionOptions: executionOptions, resources: resources);
                state.MarkOwnerCreated(slot);
            });
            faultObserver?.Invoke(TextFaultStage.OwnerFlushed);
            budget.Check();
            var provider = new TextFileStreamProvider(Root, leaf, SourceNodeId, executionOptions: executionOptions, resources: resources);
            generation = physicalGate.Run(() =>
            {
                var created = new TextGeneration(Root, leaf, SourceNodeId, scope, limitsOptions, provider, executionOptions: executionOptions, resources: resources);
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

    internal TextGeneration OpenGeneration(TextGenerationSlot slot, ReadExecutionBudget budget)
    {
        var generation = slot.Generation ?? throw TextErrors.Corrupt();
        CheckPhysical(budget);
        physicalGate.Run(() => generation.OpenIndex(budget));
        return generation;
    }

    internal void Publish(TextGeneration generation, ReadExecutionBudget budget)
    {
        var files = physicalGate.Run(() => generation.CloseAndCapture(budget));
        TextSettlement.ThrowDeferredBudgetFailure(generation);
        physicalGate.Run(() => TextFiles.CheckGenerationBound(generation.Path, budget: budget, executionOptions: executionOptions));
        budget.Check();
        physicalGate.Run(() => TextFiles.WritePendingManifest(Root, generation.Leaf, generation.Scope,
            generation.SealRecords(), files, limits, budget: budget, executionOptions: executionOptions, resources: resources));
        budget.Check();
        faultObserver?.Invoke(TextFaultStage.InventoryFlushed);
        budget.Check();
        physicalGate.Run(() => TextFiles.PublishManifest(Root, generation.Leaf, resources));
        budget.Check();
        faultObserver?.Invoke(TextFaultStage.ManifestPublished);
        CheckPhysical(budget);
        PublishVerified(generation);
    }

    internal void Retire(TextGenerationSlot slot)
    {
        Exception? failure = null;
        physicalGate.Run(() =>
        {
            try
            {
                var generation = slot.Generation ?? throw TextErrors.Corrupt();
                generation.DisposeIndex();
                TextFiles.DeleteOwnedGeneration(Root, generation.Leaf, SourceNodeId, limits, executionOptions: executionOptions, resources: resources);
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

    internal void Shutdown(TextGenerationSlot?[] slots) => shutdown.Run(slots);

    internal void CheckPhysical(ReadExecutionBudget? budget)
    {
        physicalGate.Run(() =>
        {
            state.CaptureSlots(out var first, out var second, out var third);
            TextPhysicalBudget.Check(Root, first, second, third, budget, executionOptions: executionOptions);
        });
    }

    private void PublishVerified(TextGeneration generation)
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

    private void Activate(TextGeneration generation)
    {
        generation.MarkPublished();
        faultObserver?.Invoke(TextFaultStage.GenerationActivated);
    }

    private void DeleteForReplacement(TextGenerationSlot previous, TextGeneration generation)
    {
        physicalGate.Run(() =>
        {
            try
            {
                var previousGeneration = previous.Generation ?? throw TextErrors.Corrupt();
                previousGeneration.DisposeIndex();
                TextFiles.DeleteOwnedGeneration(Root, previousGeneration.Leaf, SourceNodeId, limits, executionOptions: executionOptions, resources: resources);
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

    private void RemoveFailedBuild(TextGenerationSlot slot, string leaf, Exception primary,
        TextGeneration? generation)
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
                TextFiles.DeleteBuildingGeneration(Root, leaf, SourceNodeId, limits, executionOptions: executionOptions, resources: resources);
                slot.SharedReservation?.CompleteAfterJoinedCleanup();
                state.ClearFailedBuild(slot);
            }), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
