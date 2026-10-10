namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedTextStatisticsValidation
{
    private const int Empty = 0;
    private const string Invalid = "The distributed text statistics are invalid or incomparable.";

    internal static void Require(DistributedTextStatisticsV1 statistics, IReadOnlyList<string> terms,
        KeyLoad.Core.ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        if (statistics.Terms.IsDefault || statistics.DocumentFrequencies.IsDefault
            || statistics.DocumentCount < Empty || statistics.TotalLength < Empty
            || statistics.Terms.Length != terms.Count || statistics.DocumentFrequencies.Length != terms.Count
            || statistics.DocumentCount == Empty && statistics.TotalLength != Empty)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        for (var index = Empty; index < terms.Count; index++)
        {
            budget.Check();
            if (!StringComparer.Ordinal.Equals(statistics.Terms[index], terms[index])
                || statistics.DocumentFrequencies[index] < Empty
                || statistics.DocumentFrequencies[index] > statistics.DocumentCount)
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        }
        budget.CheckResult(statistics);
    }
}
