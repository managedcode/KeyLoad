using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Bounds logical storage work, output bytes and elapsed time within one consistent read cut.</summary>
public sealed class ReadExecutionBudget
{
    private const int EmptyElementCount = 0;

    private const string DeadlineExceeded = "The read execution deadline is exceeded.";
    private const string ReadBytesExceeded = "The read execution byte budget is exceeded.";
    private const string ExaminedRecordsExceeded = "The read execution examined-record budget is exceeded.";
    private const string GrantOwnerMismatch = "The read grant belongs to a different operation.";
    private const string TextTokensExceeded = "The search corpus token budget is exceeded.";
    private readonly DatabaseLimits limits;
    private readonly CancellationToken cancellationToken;
    private readonly TimeProvider clock;
    private readonly long started;
    private long bytes;
    private long reservedReadGrantBytes;
    private int examinedGrantRecords;
    private int reservedGrantRecords;
    private long textTokens;

    /// <summary>Starts a budget for one operation; the clock does not alter the hosting runtime.</summary>
    /// <param name="options">Native centrally validated operation limits, captured once for this read.</param>
    /// <param name="cancellationToken">Caller cancellation for all subsequent work.</param>
    /// <param name="timeProvider">Optional operation clock, defaulting to system time.</param>
    public ReadExecutionBudget(IOptions<DatabaseLimits> options, TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        limits = options.Value;
        limits.Validate();
        this.cancellationToken = cancellationToken;
        clock = timeProvider ?? TimeProvider.System;
        started = clock.GetTimestamp();
    }

    /// <summary>Gets accepted logical key/value bytes; this is not physical disk I/O.</summary>
    public long ReadBytes => bytes;

    internal CancellationToken Cancellation => cancellationToken;

    /// <summary>Wraps an existing gated view so its reads share this operation budget.</summary>
    /// <param name="view">Existing view valid only inside its owning store action.</param>
    /// <returns>A read-only budgeted view that must not escape that same action.</returns>
    public IKeyValueView CreateView(IKeyValueView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Check();
        return new BudgetedReadView(view, this);
    }

    /// <summary>Throws when cancellation or the operation deadline prevents further work.</summary>
    public void Check()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (clock.GetElapsedTime(started) > TimeSpan.FromSeconds(limits.QueryDeadlineSeconds))
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DeadlineExceeded);
        }
    }

    /// <summary>Reserves one non-borrowable raw-byte ceiling for a sequential query leaf.</summary>
    /// <param name="maximumBytes">Maximum accepted native bytes reserved for that leaf.</param>
    /// <param name="maximumRecords">Maximum native point attempts and range records reserved for that leaf.</param>
    /// <returns>A reader sharing this operation's aggregate bytes, deadline and cancellation.</returns>
    internal ReadExecutionBudgetReadGrant CreateReadGrant(long maximumBytes, int maximumRecords)
    {
        Check();
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRecords);
        if (maximumBytes > limits.MaxQueryReadBytes - bytes - reservedReadGrantBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadBytesExceeded);
        }
        if (maximumRecords > limits.MaxScanRecords - examinedGrantRecords - reservedGrantRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ExaminedRecordsExceeded);
        }
        reservedReadGrantBytes += maximumBytes;
        reservedGrantRecords += maximumRecords;
        return new(this, maximumBytes, maximumRecords);
    }

    internal void ChargeReadGrant(ReadExecutionBudgetReadGrant grant, long count)
    {
        Check();
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (!grant.BelongsTo(this))
        {
            throw new ArgumentException(GrantOwnerMismatch, nameof(grant));
        }
        if (count > grant.RemainingBytes || count > reservedReadGrantBytes
            || count > limits.MaxQueryReadBytes - bytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReadBytesExceeded);
        }
        if (grant.RemainingRecords == EmptyElementCount || reservedGrantRecords == EmptyElementCount
            || examinedGrantRecords >= limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ExaminedRecordsExceeded);
        }
        bytes += count;
        reservedReadGrantBytes -= count;
        examinedGrantRecords++;
        reservedGrantRecords--;
        grant.Accept(count);
    }

    /// <summary>Accepts examined storage bytes before the consumer allocates or decodes them.</summary>
    /// <param name="count">Nonnegative key/value work, including matching scan lookahead.</param>
    public void ChargeBytes(long count)
    {
        Check();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > limits.MaxQueryReadBytes - bytes - reservedReadGrantBytes)
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
        view.ReadValue(key, borrowed => value = borrowed.ToArray(), ChargeBytes);
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
        view.ReadValue(key, value => record = NativeSerialization.Deserialize<T>(value), ChargeBytes);
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
        var result = view.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, ChargeBytes, cancellationToken);
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
        using var counter = new ResultByteCounterStream(limits.MaxBatchBytes, Check);
        JsonSerializer.Serialize(counter, result, JsonDefaults.Options);
        Check();
        return counter.Length;
    }
}
