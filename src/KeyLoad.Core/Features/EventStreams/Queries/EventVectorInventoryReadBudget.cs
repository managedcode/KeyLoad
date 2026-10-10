using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal sealed class EventVectorInventoryReadBudget(IOptions<DatabaseLimits> options)
{
    private const long EmptyBytes = 0;
    private const int EmptyRecords = 0;
    private const int IncludedRecord = 1;
    private const string Exhausted = "The complete native event vector inventory exceeds its read bound.";
    private long bytes = EmptyBytes;
    private int records = EmptyRecords;

    internal int ScanRecords => options.Value.MaxScanRecords;

    internal void RequireSourceCount(int count)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Value.Validate();
        if (count < EmptyRecords || count > options.Value.MaxResults)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, Exhausted); }
    }

    internal void Observe(long byteCount)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        limits.Validate();
        if (byteCount < EmptyBytes)
        { throw Errors.Fail(ErrorCode.Corruption, Exhausted); }
        records = checked(records + IncludedRecord);
        bytes = checked(bytes + byteCount);
        if (records > limits.MaxScanRecords || bytes > limits.MaxBatchBytes || bytes > limits.MaxQueryReadBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, Exhausted); }
    }
}
