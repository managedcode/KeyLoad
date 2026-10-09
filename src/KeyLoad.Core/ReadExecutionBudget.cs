using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Bounds logical storage work, output bytes and elapsed time within one consistent read cut.</summary>
public sealed class ReadExecutionBudget
{

    private const string DeadlineExceeded = "The read execution deadline is exceeded.";
    internal const string ReadBytesExceeded = "The read execution byte budget is exceeded.";
    private const string TextTokensExceeded = "The search corpus token budget is exceeded.";
    private const long NoReservedReadBytes = 0;
    private readonly DatabaseLimits limits;
    private ReadExecutionBudgetScopedReadGrants? readGrants;
    private readonly CancellationToken cancellationToken;
    private readonly TimeProvider clock;
    private readonly long started;
    private TimeSpan lifetimeBound;
    private long bytes;
    private int resultBytesLimit;
    private long textTokens;
    private ReadExecutionBudgetStageCancellation? stageCancellation;

    /// <summary>Starts a budget for one operation; the clock does not alter the hosting runtime.</summary>
    /// <param name="options">Native centrally validated operation limits, captured once for this read.</param>
    /// <param name="cancellationToken">Caller cancellation for all subsequent work.</param>
    /// <param name="timeProvider">Optional operation clock, defaulting to system time.</param>
    public ReadExecutionBudget(IOptions<DatabaseLimits> options, TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        limits = options.Value;
        limits.Validate();
        lifetimeBound = TimeSpan.FromSeconds(limits.QueryDeadlineSeconds);
        resultBytesLimit = limits.MaxBatchBytes;
        this.cancellationToken = cancellationToken;
        clock = timeProvider ?? TimeProvider.System;
        started = clock.GetTimestamp();
    }

    /// <summary>Gets accepted logical key/value bytes; this is not physical disk I/O.</summary>
    public long ReadBytes => bytes;

    internal TimeSpan RemainingLifetime
    {
        get
        {
            Check();
            var remaining = lifetimeBound - clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded); }
            return remaining;
        }
    }

    internal void ConstrainLifetime(DateTimeOffset absoluteExpiry)
    {
        Check();
        var remaining = absoluteExpiry - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded); }
        var bounded = clock.GetElapsedTime(started) + remaining;
        if (bounded < lifetimeBound)
        { lifetimeBound = bounded; }
    }

    internal CancellationToken Cancellation => cancellationToken;
    internal int MaximumResultBytes => resultBytesLimit;
    internal long MaximumNativeReadBytes => limits.MaxQueryReadBytes;
    internal int MaximumNativeScanRecords => limits.MaxScanRecords;

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
        return new BudgetedReadView(view, this, readGrants?.Current);
    }

    /// <summary>Throws when cancellation or the operation deadline prevents further work.</summary>
    public void Check()
    {
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Read(ref stageCancellation)?.Check();
        if (clock.GetElapsedTime(started) > lifetimeBound)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded);
        }
    }

    internal ReadExecutionBudgetStageCancellationLease EnterStageCancellation(CancellationToken stageToken)
    {
        Check();
        var stages = LazyInitializer.EnsureInitialized(ref stageCancellation, static () => new());
        return stages.Enter(stageToken);
    }

    /// <summary>Accepts examined storage bytes before the consumer allocates or decodes them.</summary>
    /// <param name="count">Nonnegative key/value work, including matching scan lookahead.</param>
    public void ChargeBytes(long count)
    {
        Check();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > limits.MaxQueryReadBytes - bytes - (readGrants?.ReservedBytes ?? NoReservedReadBytes))
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
    {
        ArgumentNullException.ThrowIfNull(view);
        Check();
        byte[]? value = null;
        view.ReadValue(key, borrowed => value = borrowed.ToArray(), ChargeNativeReadRecord);
        Check();
        return value;
    }

    /// <summary>Decodes a record directly from gate-scoped bytes after accepting its work.</summary>
    /// <typeparam name="T">Owned decoded record type.</typeparam>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="key">Lookup key.</param>
    /// <returns>The decoded record or null for an absent key.</returns>
    public T? ReadRecord<T>(IKeyValueView view, byte[] key) where T : class
    {
        ArgumentNullException.ThrowIfNull(view);
        Check();
        T? record = null;
        view.ReadValue(key, value => record = NativeSerialization.Deserialize<T>(value), ChargeNativeReadRecord);
        Check();
        return record;
    }

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
    {
        ArgumentNullException.ThrowIfNull(view);
        Check();
        var result = view.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, ChargeNativeReadRecord, cancellationToken);
        Check();
        return result;
    }

    /// <summary>Materializes only records whose raw work passed the shared budget.</summary>
    /// <param name="view">Storage view inside its read or transaction gate.</param>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="maxRecords">Maximum delivered records before charged lookahead.</param>
    /// <param name="afterKey">Optional exclusive lower bound.</param>
    /// <returns>Independent key/value copies and the record-limit lookahead result.</returns>
    public ScanPage Scan(IKeyValueView view, byte[] prefix, int maxRecords, byte[]? afterKey = null)
    {
        var records = new List<KeyValueRecord>();
        var result = VisitRange(view, prefix, maxRecords, (key, value) =>
        {
            records.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, afterKey);
        return new([.. records], result.HasMore);
    }

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
    {
        Check();
        using var counter = new ResultByteCounterStream(resultBytesLimit, Check);
        JsonSerializer.Serialize(counter, result, JsonDefaults.Options);
        Check();
        return counter.Length;
    }
    private ReadExecutionBudgetScopedReadGrants ReadGrants => readGrants ??= new(this);
    internal long RemainingReadGrantBytes => limits.MaxQueryReadBytes - bytes - (readGrants?.ReservedBytes ?? NoReservedReadBytes);
    internal int RemainingReadGrantRecords => limits.MaxScanRecords - (readGrants?.ClaimedRecords ?? EmptyClaimedRecords);
    private const int EmptyClaimedRecords = 0;
    internal void AcceptReadGrantBytes(long count) => bytes += count;
    internal void CompleteReadGrant(ReadExecutionBudgetReadGrant grant) => ReadGrants.CompleteReadGrant(grant);
    internal ReadExecutionBudgetReadGrantLease EnterReadGrant(ReadExecutionBudgetReadGrant grant) => ReadGrants.EnterReadGrant(grant);
    internal void ExitReadGrant(ReadExecutionBudgetReadGrant grant) => ReadGrants.ExitReadGrant(grant);
    internal IKeyValueView CreateView(IKeyValueView view, ReadExecutionBudgetReadGrant grant) => ReadGrants.CreateView(view, grant);
    internal ReadExecutionBudgetReadGrant CreateReadGrant(long maximumBytes, int maximumRecords)
        => ReadGrants.CreateReadGrant(maximumBytes, maximumRecords);
    internal void ChargeReadGrant(ReadExecutionBudgetReadGrant grant, long count) => ReadGrants.ChargeReadGrant(grant, count);
    internal void ImportReadGrant(ReadExecutionBudgetReadGrant grant, long count, int records)
        => ReadGrants.ImportReadGrant(grant, count, records);
    internal void ChargeNativeReadRecord(long count)
    {
        if (readGrants is { } grants)
        { grants.ChargeNativeReadRecord(count); }
        else
        { ChargeBytes(count); }
    }

}
