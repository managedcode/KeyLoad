using System.Runtime.ExceptionServices;

namespace KeyLoad.Core.ResourceExecution.Execution;

/// <summary>Owns caller cancellation, explicit absolute expiries, cancellation stages and first uncertain failure.</summary>
internal struct ReadExecutionBudgetLifetime
{
    private const string DeadlineExceeded = "The read execution deadline is exceeded.";
    private readonly CancellationToken cancellationToken;
    private readonly TimeProvider clock;
    private readonly long started;
    private TimeSpan? lifetimeBound;
    private DateTimeOffset expiresAt;
    private ReadExecutionBudgetStageCancellation? stageCancellation;
    private ExceptionDispatchInfo? indeterminateMetadata;

    internal ReadExecutionBudgetLifetime(TimeProvider? timeProvider, CancellationToken cancellationToken)
    {
        this.cancellationToken = cancellationToken;
        clock = timeProvider ?? TimeProvider.System;
        started = clock.GetTimestamp();
        lifetimeBound = null;
        expiresAt = DateTimeOffset.MaxValue;
        stageCancellation = null;
        indeterminateMetadata = null;
    }

    /// <summary>Gets the earliest explicit absolute expiry without reminting it from remaining monotonic time.</summary>
    internal DateTimeOffset ExpiresAt
    {
        get
        {
            Check();
            return expiresAt;
        }
    }

    /// <summary>Gets the time left before an explicit expiry, or an infinite span when only cancellation applies.</summary>
    internal TimeSpan RemainingLifetime
    {
        get
        {
            Check();
            if (lifetimeBound is not { } bound)
            {
                return Timeout.InfiniteTimeSpan;
            }
            var remaining = bound - clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded);
            }
            return remaining;
        }
    }

    internal void ConstrainLifetime(DateTimeOffset absoluteExpiry)
    {
        Check();
        var remaining = absoluteExpiry - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded);
        }
        var bounded = clock.GetElapsedTime(started) + remaining;
        if (lifetimeBound is not { } current || bounded < current)
        {
            lifetimeBound = bounded;
        }
        if (absoluteExpiry < expiresAt)
        {
            expiresAt = absoluteExpiry;
        }
    }

    internal readonly CancellationToken Cancellation => cancellationToken;

    /// <summary>Throws when cancellation or an explicit expiry prevents further work.</summary>
    /// <remarks>Every check observes the operation's monotonic clock, as the elapsed work is measured on the caller's time provider whether or not an expiry is attached.</remarks>
    internal void Check()
    {
        Volatile.Read(ref indeterminateMetadata)?.Throw();
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Read(ref stageCancellation)?.Check();
        var elapsed = clock.GetElapsedTime(started);
        if (lifetimeBound is { } bound && elapsed > bound)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded);
        }
    }

    internal bool HasIndeterminateMetadata => Volatile.Read(ref indeterminateMetadata) is not null;

    /// <summary>Retains an uncertain dispatched frame failure without inventing observed work or renewing this ledger.</summary>
    internal void FailIndeterminateMetadata(Exception original)
    {
        ArgumentNullException.ThrowIfNull(original);
        _ = Interlocked.CompareExchange(ref indeterminateMetadata, ExceptionDispatchInfo.Capture(original), null);
    }

    internal ReadExecutionBudgetStageCancellationLease EnterStageCancellation(CancellationToken stageToken)
    {
        Check();
        var stages = LazyInitializer.EnsureInitialized(ref stageCancellation, static () => new());
        return stages.Enter(stageToken);
    }

}
