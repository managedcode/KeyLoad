using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal sealed class SearchRankFusion(int constant, int limit, ReadExecutionBudget budget)
{
    private readonly Dictionary<EntityRef, double> scores = [];

    public void AddBranch(SearchScore[] branch, double weight)
    {
        for (var rank = 0; rank < branch.Length; rank++)
        {
            budget.Check();
            var reference = branch[rank].Reference;
            scores[reference] = scores.GetValueOrDefault(reference) + weight / ((double)constant + rank + 1);
        }
    }

    public SearchScore[] Select()
    {
        var comparer = new WorstFirstComparer();
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
        return selected.UnorderedItems.Select(item => item.Element)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Reference.Id, StringComparer.Ordinal).ToArray();
    }

    private sealed class WorstFirstComparer : IComparer<SearchScore>
    {
        public int Compare(SearchScore left, SearchScore right)
        {
            var score = left.Score.CompareTo(right.Score);
            return score != 0 ? score : -StringComparer.Ordinal.Compare(left.Reference.Id, right.Reference.Id);
        }
    }
}
