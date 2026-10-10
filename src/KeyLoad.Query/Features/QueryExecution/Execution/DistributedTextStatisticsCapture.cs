using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedTextStatisticsCapture
{
    private const int OwnedArrays = 2;
    private const string Exhausted = "The distributed statistics exceed their original query grant.";

    internal static DistributedTextStatisticsV1 Copy(string[] terms, int documents, long length,
        int[] frequencies, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(frequencies);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var bytes = checked(OwnedArrays * PartitionQueryRetention.ArrayDescriptorBytes
            + (long)terms.Length * PartitionQueryRetention.PointerBytes
            + (long)frequencies.Length * sizeof(int));
        if (bytes > budget.MaximumResultBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Exhausted); }
        budget.ChargeBytes(bytes);
        var result = new DistributedTextStatisticsV1([.. terms], documents, length, [.. frequencies]);
        DistributedTextStatisticsValidation.Require(result, terms, budget);
        return result;
    }
}
