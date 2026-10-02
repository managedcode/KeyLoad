using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.Search;

internal static class VectorRanker
{
    public static SearchScore[] Rank(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        SearchRequest request, PreparedSimilarity similarity, ReadExecutionBudget budget)
    {
        var ranked = new List<SearchScore>();
        database.VisitVisibleVectors(view, principal, request.Partition, request.Collection, request.VectorField!, budget,
            (document, vector) =>
            {
                budget.Check();
                if (vector.Space == request.Space)
                {
                    ranked.Add(new(document.Reference, similarity.Score(vector.Values.AsMemory())));
                }
            });
        return ranked.OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Reference.Id, StringComparer.Ordinal).ToArray();
    }
}
