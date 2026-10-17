using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal static class Bm25CorpusFamilyAdmission
{
    private const long InitialExaminedBytes = 0;
    private const int ProposedRecordCount = 1;
    private const int RetainedRecordIncrement = 1;

    internal static void Require(IKeyValueView view, byte[] key, Bm25CorpusCurrentV1 current,
        DatabaseLimits limits)
    {
        var bytes = checked(key.Length + NativeSerialization.Measure(current));
        var examinedBytes = InitialExaminedBytes;
        var records = ProposedRecordCount;
        var scan = view.VisitRange(Bm25CorpusKeys.Prefix(current.Partition), limits.MaxScanRecords,
            (storedKey, value) =>
            {
                if (!storedKey.SequenceEqual(key))
                {
                    bytes = checked(bytes + storedKey.Length + value.Length);
                    records = checked(records + RetainedRecordIncrement);
                }
                if (bytes > limits.MaxBatchBytes || records > limits.MaxScanRecords)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, Bm25CorpusProtocol.Capacity);
                }
                return true;
            }, observer: count =>
            {
                examinedBytes = checked(examinedBytes + count);
                if (examinedBytes > limits.MaxQueryReadBytes)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, Bm25CorpusProtocol.Capacity);
                }
            });
        if (scan.HasMore || bytes > limits.MaxBatchBytes || records > limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Bm25CorpusProtocol.Capacity);
        }
    }
}
