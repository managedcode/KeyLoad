using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjection : ITextProjection
{
    private readonly string root;
    private readonly DatabaseLimits limits;
    private readonly Guid sourceNodeId;
    private readonly Func<string, ulong> tokenHash;
    private readonly Action<NativeTextFaultStage>? faultObserver;
    private readonly SemaphoreSlim leaseGate = new(1, 1);
    private NativeTextGeneration? current;
    private NativeTextGeneration? unsettled;
    private volatile bool disposed;
    private int disposeStarted;

    internal NativeTextProjection(string directory, DatabaseLimits limits, Guid sourceNodeId,
        Func<string, ulong>? tokenHash = null, Action<NativeTextFaultStage>? faultObserver = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        root = NativeTextFiles.InitializeRoot(directory, sourceNodeId, limits);
        this.limits = limits;
        this.sourceNodeId = sourceNodeId;
        this.tokenHash = tokenHash ?? NativeTextHash.Sha256;
        this.faultObserver = faultObserver;
    }

    public ITextProjectionLease Acquire(TextProjectionScope scope, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(budget);
        NativeTextValidation.ValidateScope(scope, sourceNodeId);
        budget.Check();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!leaseGate.Wait(0))
        {
            throw NativeTextErrors.Busy();
        }
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (unsettled is not null)
            {
                throw NativeTextErrors.Corrupt();
            }
            if (current is { } cached && cached.Scope == scope)
            {
                cached.OpenIndex(budget);
                unsettled = cached;
                return new NativeTextProjectionLease(this, cached, budget, tokenHash, faultObserver, building: false);
            }
            var generation = CreateGeneration(scope, budget);
            unsettled = generation;
            return new NativeTextProjectionLease(this, generation, budget, tokenHash, faultObserver, building: true);
        }
        catch (Exception)
        {
            leaseGate.Release();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }
        disposed = true;
        leaseGate.Wait();
        try
        {
            var failures = new List<Exception>();
            var active = current;
            var pending = unsettled;
            ObserveDispose(active, failures);
            if (pending is not null && !ReferenceEquals(pending, active))
            {
                ObserveDispose(pending, failures);
            }
            if (active?.CurrentIndex is null)
            {
                current = null;
            }
            if (pending?.CurrentIndex is null)
            {
                unsettled = null;
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
        finally
        {
            leaseGate.Release();
            leaseGate.Dispose();
        }
    }

    internal void Release(NativeTextGeneration generation, ReadExecutionBudget budget, bool building, bool invalidate,
        bool completed)
    {
        try
        {
            NativeTextSettlement.Release(root, limits, generation, sourceNodeId, budget, building, invalidate,
                completed, ReferenceEquals(current, generation), () => current = null, faultObserver);
            unsettled = null;
        }
        finally
        {
            leaseGate.Release();
        }
    }

    internal void Publish(NativeTextGeneration generation, ReadExecutionBudget budget)
    {
        var files = generation.CloseAndCapture(budget);
        NativeTextSettlement.ThrowDeferredBudgetFailure(generation);
        NativeTextFiles.CheckGenerationBound(generation.Path, budget);
        budget.Check();
        NativeTextFiles.WritePendingManifest(root, generation.Leaf, generation.Scope,
            generation.SealRecords(), files, limits, budget);
        budget.Check();
        faultObserver?.Invoke(NativeTextFaultStage.NativeInventoryFlushed);
        budget.Check();
        NativeTextFiles.PublishManifest(root, generation.Leaf);
        budget.Check();
        faultObserver?.Invoke(NativeTextFaultStage.ManifestPublished);
        NativeTextFiles.CheckGenerationBound(generation.Path, budget);
        budget.Check();
        generation.MarkPublished();
        var previous = current;
        current = generation;
        faultObserver?.Invoke(NativeTextFaultStage.GenerationActivated);
        if (previous is not null)
        {
            previous.DisposeIndex();
            NativeTextFiles.DeleteOwnedGeneration(root, previous.Leaf, sourceNodeId, limits);
        }
    }

    private NativeTextGeneration CreateGeneration(TextProjectionScope scope, ReadExecutionBudget budget)
    {
        var leaf = NativeTextValidation.GenerationLeaf();
        try
        {
            NativeTextFiles.WriteOwner(root, leaf, sourceNodeId, scope, limits);
            faultObserver?.Invoke(NativeTextFaultStage.OwnerFlushed);
            budget.Check();
            var provider = new NativeTextFileStreamProvider(root, leaf, sourceNodeId);
            return new NativeTextGeneration(root, leaf, sourceNodeId, scope, limits, provider);
        }
        catch (Exception error)
        {
            try
            {
                NativeTextFiles.DeleteBuildingGeneration(root, leaf, sourceNodeId, limits);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(error, cleanupFailure);
            }
            throw;
        }
    }

    private void ObserveDispose(NativeTextGeneration? generation, List<Exception> failures)
    {
        if (generation is null)
        {
            return;
        }
        if (generation.Published && generation.CurrentIndex is not null)
        {
            ServerFailureObserver.Observe(() => NativeTextSettlement.Refresh(root, limits, generation,
                budget: null, faultObserver: faultObserver), failures);
        }
        if (generation.CurrentIndex is not null)
        {
            ServerFailureObserver.Observe(generation.DisposeIndex, failures);
        }
    }

}
