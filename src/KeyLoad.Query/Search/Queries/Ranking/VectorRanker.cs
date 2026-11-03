using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query.Search;

internal static class VectorRanker
{
    public static SearchScore[] Rank(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        SearchRequest request, PreparedSimilarity similarity, ReadExecutionBudget budget,
        FilteredSearchEligibility eligibility, PrincipalRecord? sourcePrincipal = null, int? retainedLimit = null)
    {
        var ranked = new List<SearchScore>();
        var comparer = GlobalBranchOrder.WorstFirst(budget);
        var selected = retainedLimit.HasValue ? new PriorityQueue<SearchScore, SearchScore>(comparer) : null;
        VisibleVectorReads.VisitPacked(database, view, principal, request.Partition, request.Collection, request.VectorField!, budget,
            (document, vector, _) =>
            {
                budget.Check();
                if ((sourcePrincipal is null || database.Authorization.CanReadRow(sourcePrincipal, document.Access))
                    && vector.Space == request.Space && eligibility.Allows(document.Reference.Id))
                {
                    var candidate = new SearchScore(document.Reference, similarity.ScoreBorrowed(vector.Coordinates()));
                    if (selected is null)
                    {
                        ranked.Add(candidate);
                    }
                    else
                    {
                        Offer(selected, candidate, retainedLimit!.Value, comparer);
                    }
                }
            });
        var result = selected is null ? ranked.ToArray() : selected.UnorderedItems.Select(item => item.Element).ToArray();
        GlobalBranchOrder.SortScores(result, budget);
        return result;
    }

    private static void Offer(PriorityQueue<SearchScore, SearchScore> selected, SearchScore candidate,
        int limit, IComparer<SearchScore> comparer)
    {
        if (selected.Count < limit)
        {
            selected.Enqueue(candidate, candidate);
        }
        else if (comparer.Compare(candidate, selected.Peek()) > 0)
        {
            selected.DequeueEnqueue(candidate, candidate);
        }
    }
}
