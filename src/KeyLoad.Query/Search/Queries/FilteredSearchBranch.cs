using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class FilteredSearchBranch
{
    internal static SearchScore[] Apply(SearchScore[] ranked, FilteredSearchEligibility eligibility,
        ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(budget);
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
