using KeyLoad.Core.Features.GraphTraversal.Models;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal.Serialization;

internal static class GraphCrossPartitionCapacityWriter
{
    private const string MissingCounter = "Graph projection capacity metadata is missing for stored rows.";
    private const string InvalidCounter = "Graph projection capacity metadata is malformed.";
    private const string CapacityExceeded = "The graph projection capacity is exhausted.";

    internal static void Apply(IAtomicTransaction transaction, PartitionRef partition,
        GraphCrossPartitionCapacityDirection direction, byte[] rowPrefix,
        IReadOnlyList<GraphCrossPartitionRecordWrite> writes, DatabaseLimits limits)
    {
        var counterKey = GraphCrossPartitionKeys.Capacity(partition, direction);
        var counter = GraphCrossPartitionRecords.Read<GraphCrossPartitionCapacityV1>(transaction, counterKey);
        if (counter is not null)
        {
            GraphCrossPartitionValidation.ValidateCapacity(counter, direction);
            if (counter.RecordCount > limits.MaxScanRecords || counter.EncodedBytes > limits.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidCounter);
            }
        }
        else if (HasAnyRows(transaction, rowPrefix))
        {
            throw Errors.Fail(ErrorCode.Corruption, MissingCounter);
        }

        var delta = MeasureDelta(transaction, writes, limits.MaxBatchBytes);
        var currentCount = counter?.RecordCount ?? 0;
        var currentBytes = counter?.EncodedBytes ?? 0;
        var next = CalculateNext(currentCount, currentBytes, delta);
        RequireCapacity(next.RecordCount, next.EncodedBytes, limits);

        PersistWrites(transaction, writes);
        if (delta.RecordCount != 0 || delta.EncodedBytes != 0 || counter is null)
        {
            transaction.PutRecord(counterKey, new GraphCrossPartitionCapacityV1(
                GraphCrossPartitionProtocol.CurrentVersion, direction, next.RecordCount, next.EncodedBytes));
        }
    }

    private static (long RecordCount, long EncodedBytes) CalculateNext(long currentCount,
        long currentBytes, (long RecordCount, long EncodedBytes) delta)
    {
        long nextCount;
        long nextBytes;
        try
        {
            nextCount = checked(currentCount + delta.RecordCount);
            nextBytes = checked(currentBytes + delta.EncodedBytes);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCounter);
        }
        if (nextCount < 0 || nextBytes < 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCounter);
        }
        return (nextCount, nextBytes);
    }

    private static void RequireCapacity(long nextCount, long nextBytes, DatabaseLimits limits)
    {
        if (nextCount > limits.MaxScanRecords || nextBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CapacityExceeded);
        }
    }

    private static (long RecordCount, long EncodedBytes) MeasureDelta(IAtomicTransaction transaction,
        IReadOnlyList<GraphCrossPartitionRecordWrite> writes, int maxBytes)
    {
        long recordCount = 0;
        long encodedBytes = 0;
        foreach (var write in writes)
        {
            var currentLength = ExistingLength(transaction, write.Key);
            recordCount = checked(recordCount + RecordDelta(currentLength, write.Value));
            encodedBytes = checked(encodedBytes + ByteDelta(currentLength, write.Value, maxBytes));
        }
        return (recordCount, encodedBytes);
    }

    private static int RecordDelta(int? currentLength, byte[]? nextValue)
        => currentLength.HasValue ? nextValue is null ? -1 : 0 : nextValue is null ? 0 : 1;

    private static long ByteDelta(int? currentLength, byte[]? nextValue, int maxBytes)
    {
        if (nextValue is { Length: var length } && length > maxBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CapacityExceeded);
        }
        return (nextValue?.LongLength ?? 0) - currentLength.GetValueOrDefault();
    }

    private static int? ExistingLength(IAtomicTransaction transaction, byte[] key)
    {
        int? length = null;
        transaction.ReadValue(key, bytes => length = bytes.Length);
        return length;
    }

    private static bool HasAnyRows(IAtomicTransaction transaction, byte[] prefix)
    {
        var result = transaction.VisitRange(prefix, 1, static (_, _) => false);
        return result.Records != 0;
    }

    private static void PersistWrites(IAtomicTransaction transaction,
        IReadOnlyList<GraphCrossPartitionRecordWrite> writes)
    {
        foreach (var write in writes)
        {
            if (write.Value is null)
            {
                transaction.Delete(write.Key);
            }
            else
            {
                transaction.Put(write.Key, write.Value);
            }
        }
    }
}
