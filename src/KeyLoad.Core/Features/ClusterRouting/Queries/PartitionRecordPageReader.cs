using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Queries;

/// <summary>Reads exact bounded raw records from a caller-owned committed view.</summary>
internal static class PartitionRecordPageReader
{
    private const int InitialRetainedBytes = 0;
    private const int InitialExaminedBytes = 0;
    private const int MinimumNativeReadBytes = 0;
    private const int EmptyPageRecordCount = 0;
    private const int MinimumExaminedByteCount = 0;

    private const int LastReturnedRecordOffset = 1;

    private const string ExaminedBudgetExceeded = "The partition record page exceeds its examined-byte budget.";
    internal const string RetainedBudgetExceeded = "The partition record page exceeds its retained-byte budget.";
    private const string InvalidNativeAccounting = "The storage view returned inconsistent range accounting.";

    internal static PartitionRecordPage Read(IKeyValueView view, PartitionRef partition,
        string family, int maxRecords, long maxRetainedBytes, long maxExaminedBytes,
        ReadOnlyMemory<byte> afterKey = default, CancellationToken cancellationToken = default)
        => ReadCore(view, partition, family, maxRecords, maxRetainedBytes, maxExaminedBytes,
            null, afterKey, cancellationToken);

    internal static PartitionRecordPage Read(IKeyValueView view, PartitionRef partition,
        string family, int maxRecords, long maxRetainedBytes, long maxExaminedBytes,
        StorageReadObserver observer, ReadOnlyMemory<byte> afterKey = default,
        CancellationToken cancellationToken = default)
        => ReadCore(view, partition, family, maxRecords, maxRetainedBytes, maxExaminedBytes,
            observer, afterKey, cancellationToken);

    private static PartitionRecordPage ReadCore(IKeyValueView view, PartitionRef partition,
        string family, int maxRecords, long maxRetainedBytes, long maxExaminedBytes,
        StorageReadObserver? observer, ReadOnlyMemory<byte> afterKey, CancellationToken cancellationToken)
    {

        cancellationToken.ThrowIfCancellationRequested();
        var prefix = PartitionRecordValidation.ValidateRequest(view, partition, family,
            maxRecords, maxRetainedBytes, maxExaminedBytes, afterKey, out var ownedAfterKey);
        var records = new List<KeyValueRecord>();
        long retainedBytes = InitialRetainedBytes;
        long examinedBytes = InitialExaminedBytes;

        void ChargeExamined(long byteCount)
        {

            cancellationToken.ThrowIfCancellationRequested();
            if (byteCount < MinimumExaminedByteCount || byteCount > maxExaminedBytes - examinedBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ExaminedBudgetExceeded);
            }

            examinedBytes = checked(examinedBytes + byteCount);
            observer?.Invoke(byteCount);
            cancellationToken.ThrowIfCancellationRequested();
        }

        bool RetainRecord(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recordBytes = checked((long)key.Length + value.Length);
            if (recordBytes > maxRetainedBytes - retainedBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, RetainedBudgetExceeded);
            }

            retainedBytes = checked(retainedBytes + recordBytes);
            records.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }

        var range = view.VisitRange(prefix, maxRecords, RetainRecord, ownedAfterKey,
            observer: ChargeExamined, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (range.ReadBytes != examinedBytes || range.ReadBytes < MinimumNativeReadBytes
            || range.Records != records.Count || range.StoppedByVisitor
            || (range.HasMore && records.Count == EmptyPageRecordCount))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidNativeAccounting);
        }

        ReadOnlyMemory<byte>? continuation = null;
        if (range.HasMore)
        {
            var continuationKey = records[^LastReturnedRecordOffset].Key;
            ReserveContinuation(continuationKey.Length, maxRetainedBytes, ref retainedBytes);
            continuation = continuationKey.ToArray();
        }

        return new(ImmutableArray.CreateRange(records), range.HasMore,
            retainedBytes, examinedBytes, continuation);
    }

    private static void ReserveContinuation(int byteCount, long maxRetainedBytes, ref long retainedBytes)
    {
        if (byteCount > maxRetainedBytes - retainedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RetainedBudgetExceeded);
        }

        retainedBytes = checked(retainedBytes + byteCount);
    }
}
