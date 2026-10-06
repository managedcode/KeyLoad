using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextProjection : ITextProjection
{
    private const int FailureEmptyCount = 0;
    private const int FailureSingleItemCount = 1;
    private const int FailureIndexEmptyCount = 0;

    private readonly IOptions<NativeTextExecutionOptions> executionOptions;

    private readonly DatabaseLimits limits;
    private readonly NativeTextProjectionState state;
    private readonly NativeTextProjectionPhysicalGate physicalGate = new();
    private readonly NativeTextProjectionLifecycle lifecycle;
    private readonly Func<string, ulong> tokenHash;
    private readonly Action<NativeTextFaultStage>? faultObserver;
    private readonly Lock disposeSync = new();

    internal NativeTextProjection(string directory, IOptions<DatabaseLimits> limitsOptions, Guid sourceNodeId, IOptions<NativeTextExecutionOptions> executionOptions, Func<string, ulong>? tokenHash = null, Action<NativeTextFaultStage>? faultObserver = null)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionOptions.Value.Validate();
        this.executionOptions = executionOptions;
        ArgumentNullException.ThrowIfNull(limitsOptions);
        limits = limitsOptions.Value;
        limits.Validate();
        state = new(executionOptions: executionOptions);
        var root = NativeTextFiles.InitializeRoot(directory, sourceNodeId, limits, executionOptions: executionOptions);
        this.tokenHash = tokenHash ?? NativeTextHash.Sha256;
        this.faultObserver = faultObserver;
        lifecycle = new(root, limitsOptions, sourceNodeId, state, physicalGate, faultObserver, executionOptions: executionOptions);
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
            () => state.ClearCurrent(generation), () => state.ClearFailedBuild(slot), faultObserver, physicalGate, executionOptions: executionOptions),
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
        => failures.Count switch { FailureEmptyCount => null, FailureSingleItemCount => failures[FailureIndexEmptyCount], _ => new AggregateException(failures) };
}
