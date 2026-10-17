using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal sealed class SubscriptionWindowAuthorityReadBudget(IOptions<DatabaseLimits> options)
{
    private const int InitialRecords = 0;
    private const long InitialBytes = 0;
    private const int OneRecord = 1;
    private const string Exceeded = "The subscription window authority read budget is exceeded.";
    private readonly DatabaseLimits limits = options.Value;
    private int records = InitialRecords;
    private long bytes = InitialBytes;

    internal void Observe(long byteCount)
    {
        if (byteCount < InitialBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, Exceeded);
        }
        if (records >= limits.MaxScanRecords || byteCount > limits.MaxBatchBytes - bytes
            || byteCount > limits.MaxQueryReadBytes - bytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Exceeded);
        }
        records += OneRecord;
        bytes += byteCount;
    }
}
