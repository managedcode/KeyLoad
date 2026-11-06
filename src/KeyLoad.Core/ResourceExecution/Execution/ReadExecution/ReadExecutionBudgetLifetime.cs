using System.Runtime.ExceptionServices;

namespace KeyLoad.Core.ResourceExecution.Execution;

/// <summary>Owns caller cancellation, cancellation stages and the first uncertain failure of one operation.</summary>
internal struct ReadExecutionBudgetLifetime
{
    private readonly CancellationToken cancellationToken;
    private ReadExecutionBudgetStageCancellation? stageCancellation;
    private ExceptionDispatchInfo? indeterminateMetadata;

    internal ReadExecutionBudgetLifetime(CancellationToken cancellationToken)
    {
        this.cancellationToken = cancellationToken;
        stageCancellation = null;
        indeterminateMetadata = null;
    }

    internal readonly CancellationToken Cancellation => cancellationToken;

    /// <summary>Throws when cancellation or a retained uncertain failure prevents further work.</summary>
    internal void Check()
    {
        Volatile.Read(ref indeterminateMetadata)?.Throw();
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Read(ref stageCancellation)?.Check();
    }

    internal bool HasIndeterminateMetadata => Volatile.Read(ref indeterminateMetadata) is not null;

    /// <summary>Retains an uncertain dispatched frame failure without inventing observed work or renewing this ledger.</summary>
    internal void FailIndeterminateMetadata(Exception original)
    {
        ArgumentNullException.ThrowIfNull(original) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        _ = Interlocked.CompareExchange(ref indeterminateMetadata, ExceptionDispatchInfo.Capture(original), null);
    }

    internal ReadExecutionBudgetStageCancellationLease EnterStageCancellation(CancellationToken stageToken)
    {
        Check();
        var stages = LazyInitializer.EnsureInitialized(ref stageCancellation, static () => new());
        return stages.Enter(stageToken);
    }
}
