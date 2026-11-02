using KeyLoad.Core;

namespace KeyLoad.Query.Search;

internal static class FilteredSearchBranch
{
    internal static SearchScore[] Apply(SearchScore[] ranked, FilteredSearchEligibility eligibility,
        ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(ranked) /* KLD0041: BCL null validation accepts the ranked SearchScore[] reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(eligibility) /* KLD0041: BCL null validation accepts the eligibility FilteredSearchEligibility reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: BCL null validation accepts the budget ReadExecutionBudget reference through its object parameter. */;
        if (eligibility.IsUnrestricted)
        {
            return ranked;
        }
        var filtered = new List<SearchScore>(Math.Min(ranked.Length, eligibility.Count));
        foreach (var candidate in ranked)
        {
            budget.Check();
            if (eligibility.Allows(candidate.Reference.Id))
            {
                filtered.Add(candidate);
            }
        }
        return filtered.ToArray();
    }
}
