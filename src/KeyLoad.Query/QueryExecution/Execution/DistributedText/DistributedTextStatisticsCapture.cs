using KeyLoad.Core;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedTextStatisticsCapture
{

    internal static DistributedTextStatistics Copy(string[] terms, int documents, long length,
        int[] frequencies, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(frequencies);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var result = new DistributedTextStatistics([.. terms], documents, length, [.. frequencies]);
        DistributedTextStatisticsValidation.Require(result, terms, budget);
        return result;
    }
}
