using KeyLoad.Core.Features.GraphTraversal.Models;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal.Serialization;

internal static class GraphCrossPartitionCapacityWriter
{
    private const int RemovedRecordCount = -1;
    private const int UnchangedRecordCount = 0;
    private const int AddedRecordCount = 1;

    private const string MissingCounter = "Graph projection capacity metadata is missing for stored rows.";
    private const string InvalidCounter = "Graph projection capacity metadata is malformed.";
    private const string CapacityExceeded = "The graph projection capacity is exhausted.";

    internal static void Apply(IAtomicTransaction transaction, PartitionRef partition,
        GraphCrossPartitionCapacityDirection direction, byte[] rowPrefix,
        IReadOnlyList<GraphCrossPartitionRecordWrite> writes, DatabaseLimits limits)
    {
        const int EmptyProjectionValue = 0;
        const int NoCapacityChange = 0;

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
        var currentCount = counter?.RecordCount ?? EmptyProjectionValue;
        var currentBytes = counter?.EncodedBytes ?? EmptyProjectionValue;
        var next = CalculateNext(currentCount, currentBytes, delta);
        RequireCapacity(next.RecordCount, next.EncodedBytes, limits);

        PersistWrites(transaction, writes);
        if (delta.RecordCount != NoCapacityChange || delta.EncodedBytes != NoCapacityChange || counter is null)
        {
            transaction.PutRecord(counterKey, new GraphCrossPartitionCapacityV1(
                GraphCrossPartitionProtocol.CurrentVersion, direction, next.RecordCount, next.EncodedBytes));
        }
    }

    private static (long RecordCount, long EncodedBytes) CalculateNext(long currentCount,
        long currentBytes, (long RecordCount, long EncodedBytes) delta)
    {
        const int MinimumCapacityValue = 0;

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
        if (nextCount < MinimumCapacityValue || nextBytes < MinimumCapacityValue)
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
        const int NoRecordDelta = 0;
        const int NoEncodedByteDelta = 0;

        long recordCount = NoRecordDelta;
        long encodedBytes = NoEncodedByteDelta;
        foreach (var write in writes)
        {
            var currentLength = ExistingLength(transaction, write.Key);
            recordCount = checked(recordCount + RecordDelta(currentLength, write.Value));
            encodedBytes = checked(encodedBytes + ByteDelta(currentLength, write.Value, maxBytes));
        }
        return (recordCount, encodedBytes);
    }

    private static int RecordDelta(int? currentLength, byte[]? nextValue)
        => currentLength.HasValue ? nextValue is null ? RemovedRecordCount : UnchangedRecordCount : nextValue is null ? UnchangedRecordCount : AddedRecordCount;

    private static long ByteDelta(int? currentLength, byte[]? nextValue, int maxBytes)
    {
        const int NoNextValueBytes = 0;

        if (nextValue is { Length: var length } && length > maxBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CapacityExceeded);
        }
        return (nextValue?.LongLength ?? NoNextValueBytes) - currentLength.GetValueOrDefault();
    }

    private static int? ExistingLength(IAtomicTransaction transaction, byte[] key)
    {
        int? length = null;
        transaction.ReadValue(key, bytes => length = bytes.Length);
        return length;
    }

    private static bool HasAnyRows(IAtomicTransaction transaction, byte[] prefix)
    {
        const int ExistenceProbeRecords = 1;
        const int NoMatchingRows = 0;

        var result = transaction.VisitRange(prefix, ExistenceProbeRecords, static (_, _) => false);
        return result.Records != NoMatchingRows;
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
