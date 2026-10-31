using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Carries one operation's caller cancellation and resource lifetime bindings within one consistent read cut.</summary>
/// <param name="timeProvider">Optional operation clock, defaulting to system time.</param>
/// <param name="cancellationToken">Caller cancellation for all subsequent work.</param>
public sealed partial class ReadExecutionBudget(TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
{
    private ReadExecutionBudgetLifetime executionLifetime = new(timeProvider, cancellationToken);

    internal CancellationToken Cancellation => executionLifetime.Cancellation;

    /// <summary>Wraps an existing gated view so its reads observe this operation's resource lifetimes.</summary>
    /// <param name="view">Existing view valid only inside its owning store action.</param>
    /// <returns>A read-only view that must not escape that same action.</returns>
    public IKeyValueView CreateView(IKeyValueView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Check();
        return view is ResourceEffectPreparationTransaction prepared && ReferenceEquals(prepared.Work, this)
            ? view : new BudgetedReadView(view, this);
    }

    /// <summary>Throws when cancellation or a retained uncertain failure prevents further work.</summary>
    public void Check() => executionLifetime.Check();

    internal bool HasIndeterminateMetadata => executionLifetime.HasIndeterminateMetadata;

    /// <summary>Retains an uncertain dispatched frame failure without renewing this operation.</summary>
    internal void FailIndeterminateMetadata(Exception original) => executionLifetime.FailIndeterminateMetadata(original);

    internal ReadExecutionBudgetStageCancellationLease EnterStageCancellation(CancellationToken stageToken)
        => executionLifetime.EnterStageCancellation(stageToken);

    /// <summary>Returns an owned value copy.</summary>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="key">Lookup key.</param>
    /// <returns>An independent value buffer or null for an absent record.</returns>
    public byte[]? Read(IKeyValueView view, byte[] key)
        => ReadExecutionBudgetStorageReads.Read(this, view, key);

    /// <summary>Decodes a record directly from gate-scoped bytes.</summary>
    /// <typeparam name="T">Owned decoded record type.</typeparam>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="key">Lookup key.</param>
    /// <returns>The decoded record or null for an absent key.</returns>
    public T? ReadRecord<T>(IKeyValueView view, byte[] key) where T : class
        => ReadExecutionBudgetStorageReads.ReadRecord<T>(this, view, key);

    /// <summary>Visits ordered borrowed records under this operation's cancellation.</summary>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="maxRecords">Maximum delivered records.</param>
    /// <param name="visitor">Read-only callback; must not mutate a transaction.</param>
    /// <param name="afterKey">Optional exclusive lower bound.</param>
    /// <param name="untilKey">Optional exclusive upper bound.</param>
    /// <returns>Delivered count and stop reason.</returns>
    public StorageScanResult VisitRange(IKeyValueView view, byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null)
        => ReadExecutionBudgetStorageReads.VisitRange(this, view, prefix, maxRecords, visitor, afterKey, untilKey);

    /// <summary>Visits every record of a range in provider-sized pages under this operation's cancellation.</summary>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="visitor">Read-only callback; returning false stops the traversal.</param>
    /// <param name="afterKey">Optional exclusive lower bound.</param>
    /// <param name="untilKey">Optional exclusive upper bound.</param>
    /// <returns>Delivered count and early stop across all pages.</returns>
    internal StorageScanResult VisitAll(IKeyValueView view, byte[] prefix, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null)
        => ReadExecutionBudgetStorageReads.VisitAll(this, view, prefix, visitor, afterKey, untilKey);

    /// <summary>Materializes owned copies of one ordered range page.</summary>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="maxRecords">Maximum delivered records.</param>
    /// <param name="afterKey">Optional exclusive lower bound.</param>
    /// <returns>Independent key/value copies and the record-limit lookahead result.</returns>
    public ScanPage Scan(IKeyValueView view, byte[] prefix, int maxRecords, byte[]? afterKey = null)
        => ReadExecutionBudgetStorageReads.Scan(this, view, prefix, maxRecords, afterKey);
}
