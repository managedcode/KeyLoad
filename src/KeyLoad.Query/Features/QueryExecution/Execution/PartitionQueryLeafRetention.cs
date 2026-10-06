namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class PartitionQueryLeafRetention
{
    private const int NoRetainedBytes = 0;
    private const string PartitionQueryRetentionAccountingIsInvalidDetail = "The partition query retention accounting is invalid.";

    private const string RetainedBudgetExceeded = "The partition query retained-byte budget is exceeded.";
    private readonly long maximumBytes;
    private long fixedBytes;
    private readonly long heapBytes;
    private long currentBytes;

    internal PartitionQueryLeafRetention(long maximumBytes, int limit)
    {
        this.maximumBytes = maximumBytes;
        fixedBytes = heapBytes = PartitionQueryRetention.LeafHeapReserve(limit);
        if (maximumBytes < fixedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RetainedBudgetExceeded);
        }
    }

    internal long CurrentBytes => checked(currentBytes + fixedBytes);

    internal void Reserve(long bytes)
    {
        if (bytes < NoRetainedBytes || bytes > maximumBytes - fixedBytes - currentBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RetainedBudgetExceeded);
        }

        currentBytes += bytes;
    }

    internal void ReleaseHeap()
    {
        fixedBytes = checked(fixedBytes - heapBytes);
    }

    internal void Release(long bytes)
    {
        if (bytes < NoRetainedBytes || bytes > currentBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionQueryRetentionAccountingIsInvalidDetail);
        }

        currentBytes -= bytes;
    }
}
