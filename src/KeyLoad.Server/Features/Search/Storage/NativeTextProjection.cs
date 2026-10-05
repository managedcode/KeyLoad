using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjection : ITextProjection
{
    private readonly DatabaseLimits limits;
    private readonly NativeTextProjectionState state = new();
    private readonly NativeTextProjectionPhysicalGate physicalGate = new();
    private readonly NativeTextProjectionLifecycle lifecycle;
    private readonly Func<string, ulong> tokenHash;
    private readonly Action<NativeTextFaultStage>? faultObserver;
    private readonly Lock disposeSync = new();

    internal NativeTextProjection(string directory, DatabaseLimits limits, Guid sourceNodeId,
        Func<string, ulong>? tokenHash = null, Action<NativeTextFaultStage>? faultObserver = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var root = NativeTextFiles.InitializeRoot(directory, sourceNodeId, limits);
        this.limits = limits;
        this.tokenHash = tokenHash ?? NativeTextHash.Sha256;
        this.faultObserver = faultObserver;
        lifecycle = new(root, limits, sourceNodeId, state, physicalGate, faultObserver);
    }

    public ITextProjectionLease Acquire(TextProjectionScope scope, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(budget);
        NativeTextValidation.ValidateScope(scope, lifecycle.SourceNodeId);
        budget.Check();
        var reservation = state.Reserve(scope);
        try
        {
            var generation = reservation.Building
                ? lifecycle.CreateGeneration(reservation.Slot, scope, budget)
                : lifecycle.OpenGeneration(reservation.Slot, budget);
            return new NativeTextProjectionLease(this, generation, reservation.Slot, budget, tokenHash,
                faultObserver, reservation.Building);
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            var retired = state.FailAcquire(reservation, error is AggregateException);
            if (retired is not null)
            {
                ServerFailureObserver.Observe(() => lifecycle.Retire(retired), failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public void Dispose()
    {
        lock (disposeSync)
        {
            // Lease settlement uses independent state/physical gates, so it can join while disposal is serialized.
            state.BeginShutdown();
            state.WaitForQuiet();
            lifecycle.Shutdown(state.SnapshotSlots());
        }
    }

    internal void Release(NativeTextGeneration generation, NativeTextGenerationSlot slot,
        ReadExecutionBudget budget, bool building, bool invalidate, bool completed)
    {
        var failures = new List<Exception>();
        var currentInvalidation = invalidate && state.BeginInvalidation(generation);
        ServerFailureObserver.Observe(() => NativeTextSettlement.Release(lifecycle.Root, limits, generation,
            lifecycle.SourceNodeId, budget, building, invalidate, completed, currentInvalidation,
            () => state.ClearCurrent(generation), () => state.ClearFailedBuild(slot), faultObserver, physicalGate),
            failures);
        ServerFailureObserver.Observe(() => lifecycle.CheckPhysical(completed && failures.Count == 0 ? budget : null), failures);
        var retired = state.CompleteLease(slot, Failure(failures));
        if (retired is not null)
        {
            ServerFailureObserver.Observe(() => lifecycle.Retire(retired), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void Publish(NativeTextGeneration generation, ReadExecutionBudget budget)
        => lifecycle.Publish(generation, budget);

    internal void CheckPhysical(ReadExecutionBudget budget) => lifecycle.CheckPhysical(budget);

    internal void MutatePhysical(Action mutation) => physicalGate.Run(mutation);

    private static Exception? Failure(List<Exception> failures)
        => failures.Count switch { 0 => null, 1 => failures[0], _ => new AggregateException(failures) };
}
