using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal sealed class SearchRankFusion(int constant, int limit, ReadExecutionBudget budget)
{
    private readonly Dictionary<EntityRef, double> scores = [];

    public void AddBranch(SearchScore[] branch, double weight)
    {
        branch = GlobalBranchSinglePartitionAdapter.Prepare(branch);
        for (var rank = 0; rank < branch.Length; rank++)
        {
            GlobalBranchSinglePartitionAdapter.ValidateAt(branch, rank, budget);
            var reference = branch[rank].Reference;
            scores[reference] = scores.GetValueOrDefault(reference) + weight / ((double)constant + rank + 1);
        }
    }

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
            else if (comparer.Compare(candidate, selected.Peek()) > 0)
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
