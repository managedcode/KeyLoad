using System.Runtime.ExceptionServices;

namespace KeyLoad.Core.ResourceExecution.Execution;

/// <summary>Owns the original monotonic lifetime, absolute deadline, cancellation stages and first uncertain failure.</summary>
internal struct ReadExecutionBudgetLifetime
{
    private const string DeadlineExceeded = "The read execution deadline is exceeded.";
    private readonly CancellationToken cancellationToken;
    private readonly TimeProvider clock;
    private readonly long started;
    private TimeSpan lifetimeBound;
    private DateTimeOffset expiresAt;
    private ReadExecutionBudgetStageCancellation? stageCancellation;
    private ExceptionDispatchInfo? indeterminateMetadata;

    internal ReadExecutionBudgetLifetime(TimeSpan lifetimeBound, TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        this.lifetimeBound = lifetimeBound;
        this.cancellationToken = cancellationToken;
        clock = timeProvider ?? TimeProvider.System;
        started = clock.GetTimestamp();
        expiresAt = clock.GetUtcNow() + lifetimeBound;
        stageCancellation = null;
        indeterminateMetadata = null;
    }

    /// <summary>Gets the earliest original absolute deadline without reminting it from remaining monotonic time.</summary>
    internal DateTimeOffset ExpiresAt
    {
        get
        {
            Check();
            return expiresAt;
        }
    }

    internal TimeSpan RemainingLifetime
    {
        get
        {
            Check();
            var remaining = lifetimeBound - clock.GetElapsedTime(started);
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
        if (bounded < lifetimeBound)
        {
            lifetimeBound = bounded;
        }
        if (absoluteExpiry < expiresAt)
        {
            expiresAt = absoluteExpiry;
        }
    }

    internal readonly CancellationToken Cancellation => cancellationToken;

    /// <summary>Throws when cancellation or the operation deadline prevents further work.</summary>
    internal void Check()
    {
        Volatile.Read(ref indeterminateMetadata)?.Throw();
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Read(ref stageCancellation)?.Check();
        if (clock.GetElapsedTime(started) > lifetimeBound)
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
