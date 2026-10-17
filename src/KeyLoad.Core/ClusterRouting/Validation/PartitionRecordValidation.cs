using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

/// <summary>Validates a family-scoped page request before native traversal.</summary>
internal static class PartitionRecordValidation
{
    private const string UnsupportedFamily = "The requested partition record family is not supported.";
    private const string InvalidBudget = "The partition record page bounds must be positive.";
    private const string ForeignContinuation = "The continuation key does not belong to this partition family.";
    private const string ContinuationExceedsBudget = "The continuation key exceeds the examined-byte budget.";
    private const string InvalidContinuation = "The continuation key is malformed or noncanonical.";

    internal static byte[] ValidateRequest(IKeyValueView view, PartitionRef partition, string family,
        int maxRecords, long maxRetainedBytes, long maxExaminedBytes,
        ReadOnlyMemory<byte> afterKey, out byte[]? ownedAfterKey)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(family);
        ValidateBounds(maxRecords, maxRetainedBytes, maxExaminedBytes);
        DatabaseEngine.ValidatePartition(partition);
        if (!ContainsFamily(family))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedFamily);
        }

        var prefix = KeySpace.Partition(family, partition);
        ownedAfterKey = ValidateContinuation(afterKey, prefix, maxExaminedBytes);
        return prefix;
    }

    private static void ValidateBounds(int maxRecords, long maxRetainedBytes, long maxExaminedBytes)
    {
        const int MaxRecordsValidationBoundary = 0;
        const int MaxRetainedBytesValidationBoundary = 0;
        const int MaxExaminedBytesValidationBoundary = 0;

        if (maxRecords <= MaxRecordsValidationBoundary || maxRetainedBytes <= MaxRetainedBytesValidationBoundary || maxExaminedBytes <= MaxExaminedBytesValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidBudget);
        }
    }

    private static bool ContainsFamily(string family)
    {
        const int LowInitialValue = 0;
        const int AllLengthStep = 1;
        const int HighLowScaleFactor = 2;
        const int EmptyComparison = 0;
        const int ComparisonValidationBoundary = 0;
        const int MiddleStep = 1;

        var low = LowInitialValue;
        var high = PartitionRecordFamilies.All.Length - AllLengthStep;
        while (low <= high)
        {
            var middle = low + ((high - low) / HighLowScaleFactor);
            var comparison = string.Compare(PartitionRecordFamilies.All[middle], family, StringComparison.Ordinal);
            if (comparison == EmptyComparison)
            {
                return true;
            }

            if (comparison < ComparisonValidationBoundary)
            {
                low = middle + MiddleStep;
            }
            else
            {
                high = middle - MiddleStep;
            }
        }

        return false;
    }

    private static byte[]? ValidateContinuation(ReadOnlyMemory<byte> afterKey, byte[] prefix,
        long maxExaminedBytes)
    {
        if (afterKey.IsEmpty)
        {
            return null;
        }

        var continuation = afterKey.Span;
        if (continuation.Length <= prefix.Length || !continuation.StartsWith(prefix))
        {
            throw Errors.Fail(ErrorCode.Validation, ForeignContinuation);
        }

        if (continuation.Length > maxExaminedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ContinuationExceedsBudget);
        }

        ValidateCanonicalKey(continuation);
        return continuation.ToArray();
    }

    private static void ValidateCanonicalKey(ReadOnlySpan<byte> continuation)
    {
        try
        {
            var components = KeyCodec.Decode(continuation);
            if (!KeyCodec.Encode(components).AsSpan().SequenceEqual(continuation))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidContinuation);
            }
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidContinuation);
        }
    }
}
