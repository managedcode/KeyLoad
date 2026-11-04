using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query.Search;

internal static class VectorRanker
{
    private const int EqualOrder = 0;

    public static SearchScore[] Rank(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        SearchRequest request, PreparedSimilarity similarity, ReadExecutionBudget budget,
        FilteredSearchEligibility eligibility, PrincipalRecord? sourcePrincipal = null, int? retainedLimit = null)
    {
        var ranking = new Ranking(database, request, similarity, budget, eligibility, sourcePrincipal, retainedLimit);
        VisibleVectorReads.VisitPacked(database, view, principal, request.Partition, request.Collection, request.VectorField!, budget,
            ranking.Visit);
        return ranking.Complete();
    }

    internal static async ValueTask<SearchScore[]> RankAsync(DatabaseEngine database, ICooperativeRangeReadView view,
        PrincipalRecord principal, SearchRequest request, PreparedSimilarity similarity, ReadExecutionBudget budget,
        FilteredSearchEligibility eligibility, int retainedLimit)
    {
        var ranking = new Ranking(database, request, similarity, budget, eligibility, null, retainedLimit);
        _ = await VisibleVectorReads.VisitPackedAsync(database, view, principal, request.Partition, request.Collection,
            request.VectorField!, budget, ranking.Visit);
        return ranking.Complete();
    }

    private static void Offer(PriorityQueue<SearchScore, SearchScore> selected, SearchScore candidate,
        int limit)
    {
        if (selected.Count < limit)
        {
            selected.Enqueue(candidate, candidate);
        }
        else if (GlobalBranchOrder.CompareScores(candidate, selected.Peek()) < EqualOrder)
        {
            selected.DequeueEnqueue(candidate, candidate);
        }
    }

    private sealed class Ranking
    {
        private readonly DatabaseEngine database;
        private readonly SearchRequest request;
        private readonly PreparedSimilarity similarity;
        private readonly ReadExecutionBudget budget;
        private readonly FilteredSearchEligibility eligibility;
        private readonly PrincipalRecord? sourcePrincipal;
        private readonly int? retainedLimit;
        private readonly List<SearchScore>? ranked;
        private readonly PriorityQueue<SearchScore, SearchScore>? selected;

        internal Ranking(DatabaseEngine database, SearchRequest request, PreparedSimilarity similarity,
            ReadExecutionBudget budget, FilteredSearchEligibility eligibility, PrincipalRecord? sourcePrincipal, int? retainedLimit)
        {
            this.database = database;
            this.request = request;
            this.similarity = similarity;
            this.budget = budget;
            this.eligibility = eligibility;
            this.sourcePrincipal = sourcePrincipal;
            this.retainedLimit = retainedLimit;
            selected = retainedLimit.HasValue ? new(GlobalBranchOrder.WorstFirst(budget)) : null;
            ranked = selected is null ? [] : null;
        }

        internal void Visit(DocumentRecord document, VectorStorageRecord vector, VectorRecord? _)
        {
            budget.Check();
            if ((sourcePrincipal is not null && !database.Authorization.CanReadRow(sourcePrincipal, document.Access))
                || vector.Space != request.Space || !eligibility.Allows(document.Reference.Id))
            {
                return;
            }
            var candidate = new SearchScore(document.Reference, similarity.ScoreBorrowed(vector.Coordinates()));
            if (selected is null)
            {
                ranked!.Add(candidate);
            }
            else
            {
                Offer(selected, candidate, retainedLimit!.Value);
            }
        }

        internal SearchScore[] Complete()
        {
            var result = selected is null ? ranked!.ToArray() : selected.UnorderedItems.Select(item => item.Element).ToArray();
            GlobalBranchOrder.SortScores(result, budget);
            return result;
        }
    }
}
