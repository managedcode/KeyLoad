using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal sealed class SearchRankFusion(int constant, int limit, ReadExecutionBudget budget, bool explain = false)
{
    private const int EmptyElementCount = 0;
    private const int AdjacentElementOffset = 1;
    private const int EqualOrder = 0;

    private readonly Dictionary<EntityRef, double> scores = [];

    private readonly SearchExplanationCapture? explanations = explain ? new(budget, constant) : null;

    public void AddBranch(SearchScore[] branch, double weight, SearchBranchKind kind = SearchBranchKind.Text)
    {
        branch = GlobalBranchSinglePartitionAdapter.Prepare(branch);
        for (var rank = EmptyElementCount; rank < branch.Length; rank++)
        {
            GlobalBranchSinglePartitionAdapter.ValidateAt(branch, rank, budget);
            var reference = branch[rank].Reference;
            var nativeRank = rank + AdjacentElementOffset;
            var contribution = weight / ((double)constant + rank + AdjacentElementOffset);
            scores[reference] = scores.GetValueOrDefault(reference) + contribution;
            if (weight > EmptyElementCount)
            { explanations?.Record(reference, new(kind, nativeRank, weight, contribution)); }
        }
    }

    public SearchHitExplanation? Explain(EntityRef reference) => explanations?.For(reference);

    public SearchScore[] Select()
    {
        var comparer = GlobalBranchOrder.WorstFirst(budget);
        var selected = new PriorityQueue<SearchScore, SearchScore>(comparer);
        foreach (var (reference, score) in scores)
        {
            budget.Check();
            var candidate = new SearchScore(reference, score);
            if (selected.Count < limit)
            {
                selected.Enqueue(candidate, candidate);
            }
            else if (comparer.Compare(candidate, selected.Peek()) > EqualOrder)
            {
                selected.Dequeue();
                selected.Enqueue(candidate, candidate);
            }
        }
        var result = selected.UnorderedItems.Select(item => item.Element).ToArray();
        GlobalBranchOrder.SortScores(result, budget);
        return result;
    }
}
