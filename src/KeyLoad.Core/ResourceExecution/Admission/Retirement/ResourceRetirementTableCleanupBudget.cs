using KeyLoad.Storage;
using Microsoft.Extensions.Options;
using static KeyLoad.Core.Features.ResourceExecution.ResourceRetirementTableCleanupFamilies;

namespace KeyLoad.Core.Features.ResourceExecution;

internal sealed class ResourceRetirementTableCleanupBudget
{
    private readonly long maximumKeyBytes;
    private readonly long maximumExaminedBytes;
    private long keyBytes;
    private readonly bool stopAtCapacity;
    private bool rejectedRecord;

    internal ResourceRetirementTableCleanupBudget(IOptions<DatabaseLimits> options,
        (long MaximumBytes, int MaximumRecords) readBounds, int reservedMutations, long reservedBytes, bool stopAtCapacity = false)
    {
        var limits = options.Value;
        if (reservedMutations < Empty || reservedBytes < Empty
            || reservedMutations >= limits.MaxBatchMutations || reservedBytes >= limits.MaxBatchBytes
            || limits.MaxScanRecords <= Empty || limits.MaxQueryReadBytes <= Empty
            || readBounds.MaximumRecords <= Empty || readBounds.MaximumRecords > limits.MaxScanRecords
            || readBounds.MaximumBytes <= Empty || readBounds.MaximumBytes > limits.MaxQueryReadBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
        }
        this.stopAtCapacity = stopAtCapacity;
        MaximumRecords = Math.Min(limits.MaxBatchMutations - reservedMutations, readBounds.MaximumRecords);
        maximumKeyBytes = limits.MaxBatchBytes - reservedBytes;
        maximumExaminedBytes = readBounds.MaximumBytes;
    }

    internal List<byte[]> Keys { get; } = [];
    internal List<long> ValueBytes { get; } = [];
    internal int ExaminedRecords
    {
        get; private set;
    }
    internal int MaximumRecords
    {
        get;
    }
    internal long ExaminedBytes
    {
        get; private set;
    }
    internal long DeletedBytes
    {
        get; private set;
    }
    internal bool Stopped
    {
        get; private set;
    }

    internal void Examine(long bytes)
    {
        if (bytes < Empty || bytes > maximumExaminedBytes - ExaminedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
        }
        ExaminedBytes = checked(ExaminedBytes + bytes);
        ExaminedRecords = checked(ExaminedRecords + Step);
    }

    internal bool Retain(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        // Reserve a second last-key copy for the caller's durable continuation, before retaining.
        if (key.Length > maximumKeyBytes - keyBytes || key.Length > maximumKeyBytes - keyBytes - key.Length)
        {
            if (Keys.Count == Empty)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
            }

            Stopped = true;
            rejectedRecord = true;
            return false;
        }
        keyBytes = checked(keyBytes + key.Length);
        DeletedBytes = checked(DeletedBytes + key.Length + value.Length);
        Keys.Add(key.ToArray());
        ValueBytes.Add(value.Length);
        if (stopAtCapacity && Keys.Count == MaximumRecords)
        {
            Stopped = true;
            return false;
        }
        return true;
    }

    internal void RequireAccounting(StorageScanResult range)
    {
        var expected = Keys.Count + (rejectedRecord ? Step : Empty);
        if (range.ReadBytes != ExaminedBytes || range.Records != expected || range.StoppedByVisitor != Stopped
            || range.Records > MaximumRecords || range.StoppedByVisitor && range.HasMore
            || range.HasMore && Keys.Count == Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, Accounting);
        }
    }
}
