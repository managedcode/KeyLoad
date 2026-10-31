using KeyLoad.Core;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal sealed class RuntimeTextProjection : ITextProjection
{
    private const int FailureEmptyCount = 0;
    private const int FailureSingleItemCount = 1;
    private const int FailureIndexEmptyCount = 0;

    private const int NoCleanupFailures = 0;
    private readonly IOptions<TextExecutionOptions> executionOptions;

    private readonly TextProjectionState state;
    private readonly TextProjectionPhysicalGate physicalGate = new();
    private readonly RuntimeTextProjectionLifecycle lifecycle;
    private readonly Func<string, ulong> tokenHash;
    private readonly Action<TextFaultStage>? faultObserver;
    private readonly Lock disposeSync = new();
    private readonly TextResourceOwnership? resources;

    internal RuntimeTextProjection(string directory, Guid sourceNodeId, IOptions<TextExecutionOptions> executionOptions, Func<string, ulong>? tokenHash = null, Action<TextFaultStage>? faultObserver = null, TextResourceOwnership? resources = null)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionOptions.Value.Validate();
        this.executionOptions = executionOptions;
        this.resources = resources;
        state = new(executionOptions: executionOptions);
        var root = TextFiles.InitializeRoot(directory, sourceNodeId, executionOptions: executionOptions, resources: resources);
        this.tokenHash = tokenHash ?? TextHash.Sha256;
        this.faultObserver = faultObserver;
        lifecycle = new(root, sourceNodeId, state, physicalGate, faultObserver, executionOptions: executionOptions, resources: resources);
    }

    public ITextProjectionLease Acquire(TextProjectionScope scope, ReadExecutionBudget budget)
    {
        if (resources is null)
        {
            return RuntimeAcquire(scope, budget);
        }
        var retained = resources.ReserveLease(budget);
        try
        {
            return new TextResourceProjectionLease(() => RuntimeAcquire(scope, budget), retained);
        }
        catch (Exception error)
        {
            if (error is AggregateException)
            {
                throw;
            }
            var failures = new List<Exception> { error };
            ServerFailureObserver.Observe(retained.CompleteAfterJoinedCleanup, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private TextProjectionLease RuntimeAcquire(TextProjectionScope scope, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(budget);
        TextValidation.ValidateScope(scope, lifecycle.SourceNodeId);
        budget.Check();
        var reservation = state.Reserve(scope);
        try
        {
            var generation = reservation.Building
                ? lifecycle.CreateGeneration(reservation.Slot, scope, budget)
                : lifecycle.OpenGeneration(reservation.Slot, budget);
            return new TextProjectionLease(this, generation, reservation.Slot, budget, tokenHash,
                faultObserver, reservation.Building, executionOptions);
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

    internal void Release(TextGeneration generation, TextGenerationSlot slot,
        ReadExecutionBudget budget, bool building, bool invalidate, bool completed)
    {
        var failures = new List<Exception>();
        var settle = state.BeginRelease(slot, invalidate, out var invalidated);
        var currentInvalidation = invalidated && state.BeginInvalidation(generation);
        if (settle)
        {
            ServerFailureObserver.Observe(() => TextSettlement.Release(lifecycle.Root, generation,
                lifecycle.SourceNodeId, budget, building, invalidated, completed, currentInvalidation,
                () =>
                {
                    slot.SharedReservation?.CompleteAfterJoinedCleanup();
                    state.ClearCurrent(generation);
                }, () =>
                {
                    slot.SharedReservation?.CompleteAfterJoinedCleanup();
                    state.ClearFailedBuild(slot);
                }, faultObserver, physicalGate, executionOptions: executionOptions), failures);
        }
        ServerFailureObserver.Observe(() => lifecycle.CheckPhysical(completed && failures.Count == NoCleanupFailures ? budget : null), failures);
        var retired = state.CompleteLease(slot, (Failure(failures)).Value);
        if (retired is not null)
        {
            ServerFailureObserver.Observe(() => lifecycle.Retire(retired), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void Publish(TextGeneration generation, ReadExecutionBudget budget)
        => lifecycle.Publish(generation, budget);

    internal void CheckPhysical(ReadExecutionBudget budget) => lifecycle.CheckPhysical(budget);

    internal void MutatePhysical(Action mutation) => physicalGate.Run(mutation);

    private static (bool HasValue, Exception? Value) Failure(List<Exception> failures)
        => failures.Count switch
        {
            FailureEmptyCount => (false, default(Exception?)),
            FailureSingleItemCount => (true, ((Exception?)(failures[FailureIndexEmptyCount]))),
            _ => (true, (Exception?)(new AggregateException(failures)))
        };
}
