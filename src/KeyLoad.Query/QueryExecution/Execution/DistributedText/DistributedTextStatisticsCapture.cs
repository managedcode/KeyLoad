using KeyLoad.Core;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedTextStatisticsCapture
{

    internal static DistributedTextStatistics Copy(string[] terms, int documents, long length,
        int[] frequencies, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(terms) /* KLD0041: BCL null validation accepts the terms string[] reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(frequencies) /* KLD0041: BCL null validation accepts the frequencies int[] reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: BCL null validation accepts the budget ReadExecutionBudget reference through its object parameter. */;
        budget.Check();
        var result = new DistributedTextStatistics([.. terms], documents, length, [.. frequencies]);
        DistributedTextStatisticsValidation.Require(result, terms, budget);
        return result;
    }
}
