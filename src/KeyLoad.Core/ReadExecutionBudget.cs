using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Bounds logical storage work, output bytes and elapsed time within one consistent read cut.</summary>
public sealed partial class ReadExecutionBudget
{

    internal const string ReadBytesExceeded = "The read execution byte budget is exceeded.";
    private const string TextTokensExceeded = "The search corpus token budget is exceeded.";
    private const long NoReservedReadBytes = 0;
    private readonly DatabaseLimits limits;
    private ReadExecutionBudgetScopedReadGrants? readGrants;
    private ReadExecutionBudgetLifetime executionLifetime;
    private long bytes;
    private ReadExecutionBudgetWork runtimeWork;
    private int resultBytesLimit;
    private long textTokens;

    /// <summary>Starts a budget for one operation; the clock does not alter the hosting runtime.</summary>
    /// <param name="options">Execution centrally validated operation limits, captured once for this read.</param>
    /// <param name="cancellationToken">Caller cancellation for all subsequent work.</param>
    /// <param name="timeProvider">Optional operation clock, defaulting to system time.</param>
    public ReadExecutionBudget(IOptions<DatabaseLimits> options, TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        limits = options.Value;
        limits.Validate();
        var lifetimeBound = TimeSpan.FromSeconds(limits.QueryDeadlineSeconds);
        resultBytesLimit = limits.MaxBatchBytes;
        runtimeWork = new(limits.MaxQueryReadBytes, limits.MaxScanRecords);
        executionLifetime = new(lifetimeBound, timeProvider, cancellationToken);
    }

    /// <summary>Gets accepted logical key/value bytes; this is not physical disk I/O.</summary>
    public long ReadBytes => bytes;

    /// <summary>Gets the earliest original absolute deadline without reminting it from remaining monotonic time.</summary>
    internal DateTimeOffset ExpiresAt => executionLifetime.ExpiresAt;

    internal TimeSpan RemainingLifetime => executionLifetime.RemainingLifetime;

    internal void ConstrainLifetime(DateTimeOffset absoluteExpiry) => executionLifetime.ConstrainLifetime(absoluteExpiry);

    internal CancellationToken Cancellation => executionLifetime.Cancellation;
    internal int MaximumResultBytes => lifetimeBindings?.AvailableResultBytes(resultBytesLimit) ?? resultBytesLimit;
    internal int LifetimeResultBytesLimit => resultBytesLimit;
    internal long RuntimeMaximumReadBytes => runtimeWork.MaximumBytes;
    internal int MaximumScanRecords => runtimeWork.MaximumRecords;
    internal int ConstrainedRecords => runtimeWork.ExaminedRecords + (readGrants?.ExaminedRecords ?? EmptyClaimedRecords);

    internal void ConstrainWork(long maximumBytes, int maximumRecords)
    {
        Check();
        runtimeWork.Constrain(maximumBytes, maximumRecords, bytes + (readGrants?.ReservedBytes ?? NoReservedReadBytes),
            readGrants?.ClaimedRecords ?? EmptyClaimedRecords);
    }

    internal void ImportObservation(long observedBytes, int observedRecords)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedBytes);
        Check();
        runtimeWork.RequireObservation(observedRecords, RemainingReadGrantRecords);
        ChargeBytes(observedBytes);
        runtimeWork.Record(observedRecords);
    }

    internal void ConstrainResultBytes(int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        Check();
        resultBytesLimit = Math.Min(resultBytesLimit, maximumBytes);
    }

    /// <summary>Wraps an existing gated view so its reads share this operation budget.</summary>
    /// <param name="view">Existing view valid only inside its owning store action.</param>
    /// <returns>A read-only budgeted view that must not escape that same action.</returns>
    public IKeyValueView CreateView(IKeyValueView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Check();
        return view is ResourceEffectPreparationTransaction prepared && ReferenceEquals(prepared.Work, this)
            ? view : new BudgetedReadView(view, this, readGrants?.Current);
    }

    /// <summary>Throws when cancellation or the operation deadline prevents further work.</summary>
    public void Check() => executionLifetime.Check();

    internal bool HasIndeterminateMetadata => executionLifetime.HasIndeterminateMetadata;

    /// <summary>Retains an uncertain dispatched frame failure without inventing observed work or renewing this ledger.</summary>
    internal void FailIndeterminateMetadata(Exception original) => executionLifetime.FailIndeterminateMetadata(original);

    internal ReadExecutionBudgetStageCancellationLease EnterStageCancellation(CancellationToken stageToken)
        => executionLifetime.EnterStageCancellation(stageToken);

    /// <summary>Accepts examined storage bytes before the consumer allocates or decodes them.</summary>
    /// <param name="count">Nonnegative key/value work, including matching scan lookahead.</param>
    public void ChargeBytes(long count)
    {
        Check();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > runtimeWork.MaximumBytes - bytes - (readGrants?.ReservedBytes ?? NoReservedReadBytes))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadBytesExceeded);
        }
        bytes += count;
    }

    /// <summary>Accepts one examined text token from the shared search corpus.</summary>
    public void ChargeTextToken()
    {
        Check();
        if (textTokens >= limits.MaxSearchTextTokens)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, TextTokensExceeded);
        }
        textTokens++;
    }

    /// <summary>Returns an owned value copy only after the raw work has been accepted.</summary>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="key">Lookup key.</param>
    /// <returns>An independent value buffer or null for an absent record.</returns>
    public byte[]? Read(IKeyValueView view, byte[] key)
        => ReadExecutionBudgetStorageReads.Read(this, view, key);

    /// <summary>Decodes a record directly from gate-scoped bytes after accepting its work.</summary>
    /// <typeparam name="T">Owned decoded record type.</typeparam>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="key">Lookup key.</param>
    /// <returns>The decoded record or null for an absent key.</returns>
    public T? ReadRecord<T>(IKeyValueView view, byte[] key) where T : class
        => ReadExecutionBudgetStorageReads.ReadRecord<T>(this, view, key);

    /// <summary>Visits ordered borrowed records with one cumulative operation budget.</summary>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="maxRecords">Maximum delivered records before charged lookahead.</param>
    /// <param name="visitor">Read-only callback; must not mutate a transaction.</param>
    /// <param name="afterKey">Optional exclusive lower bound.</param>
    /// <param name="untilKey">Optional exclusive upper bound.</param>
    /// <returns>Delivered count, stop reason and examined logical storage bytes.</returns>
    public StorageScanResult VisitRange(IKeyValueView view, byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null)
        => ReadExecutionBudgetStorageReads.VisitRange(this, view, prefix, maxRecords, visitor, afterKey, untilKey);

    /// <summary>Materializes only records whose raw work passed the shared budget.</summary>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="maxRecords">Maximum delivered records before charged lookahead.</param>
    /// <param name="afterKey">Optional exclusive lower bound.</param>
    /// <returns>Independent key/value copies and the record-limit lookahead result.</returns>
    public ScanPage Scan(IKeyValueView view, byte[] prefix, int maxRecords, byte[]? afterKey = null)
        => ReadExecutionBudgetStorageReads.Scan(this, view, prefix, maxRecords, afterKey);

    /// <summary>Checks exact JSON output bytes without allocating the complete serialized result.</summary>
    /// <typeparam name="T">Response type using the current wire serializer options.</typeparam>
    /// <param name="result">Owned result whose protocol bytes must fit the response limit.</param>
    public void CheckResult<T>(T result)
        => MeasureResult(result);

    /// <summary>Counts exact bounded JSON bytes for incremental result selection.</summary>
    /// <typeparam name="T">Value type using the current wire serializer options.</typeparam>
    /// <param name="result">Value whose complete serialized bytes must fit the response limit.</param>
    /// <returns>The exact serialized UTF-8 byte count.</returns>
    public long MeasureResult<T>(T result)
        => ResultByteCounterStream.Measure(this, result);
    private ReadExecutionBudgetScopedReadGrants ReadGrants => readGrants ??= new(this);
    internal long RemainingReadGrantBytes => runtimeWork.MaximumBytes - bytes - (readGrants?.ReservedBytes ?? NoReservedReadBytes);
    internal int RemainingReadGrantRecords => runtimeWork.MaximumRecords - runtimeWork.ExaminedRecords - (readGrants?.ClaimedRecords ?? EmptyClaimedRecords);
    private const int EmptyClaimedRecords = 0;
    internal void AcceptReadGrantBytes(long count) => bytes += count;
    internal void CompleteReadGrant(ReadExecutionBudgetReadGrant grant) => ReadGrants.CompleteReadGrant(grant);
    internal void ImportSettledReadGrant(ReadExecutionBudgetReadGrant grant, long count, int records)
        => ReadGrants.ImportSettledReadGrant(grant, count, records);
    internal void CompleteSettledReadGrant(ReadExecutionBudgetReadGrant grant)
        => ReadGrants.CompleteSettledReadGrant(grant);
    internal ReadExecutionBudgetReadGrantLease EnterReadGrant(ReadExecutionBudgetReadGrant grant) => ReadGrants.EnterReadGrant(grant);
    internal void ExitReadGrant(ReadExecutionBudgetReadGrant grant) => ReadGrants.ExitReadGrant(grant);
    internal IKeyValueView CreateView(IKeyValueView view, ReadExecutionBudgetReadGrant grant) => ReadGrants.CreateView(view, grant);
    internal ReadExecutionBudgetReadGrant CreateReadGrant(long maximumBytes, int maximumRecords)
        => ReadGrants.CreateReadGrant(maximumBytes, maximumRecords);
    internal void ChargeReadGrant(ReadExecutionBudgetReadGrant grant, long count) => ReadGrants.ChargeReadGrant(grant, count);
    internal void ImportReadGrant(ReadExecutionBudgetReadGrant grant, long count, int records)
        => ReadGrants.ImportReadGrant(grant, count, records);
    internal void ChargeReadRecord(long count)
    {
        if (readGrants is { } grants)
        {
            grants.ChargeReadRecord(count);
        }
        else
        {
            ChargeUnscopedRecord(count);
        }
    }

    internal void ChargeUnscopedRecord(long count)
    {
        runtimeWork.RequireRecord(RemainingReadGrantRecords);
        ChargeBytes(count);
        runtimeWork.Record(ReadExecutionBudgetWork.SingleRecord);
    }

}
