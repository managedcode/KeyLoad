using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class GlobalBranchOrder
{
    internal static int CompareReferences(EntityRef left, EntityRef right)
    {
        var order = StringComparer.Ordinal.Compare(left.Partition.TenantId, right.Partition.TenantId);
        if (order != 0)
        {
            return order;
        }
        order = StringComparer.Ordinal.Compare(left.Partition.DatabaseId, right.Partition.DatabaseId);
        if (order != 0)
        {
            return order;
        }
        order = StringComparer.Ordinal.Compare(left.Partition.TransactionDomainId, right.Partition.TransactionDomainId);
        if (order != 0)
        {
            return order;
        }
        order = StringComparer.Ordinal.Compare(left.Partition.PartitionKey, right.Partition.PartitionKey);
        if (order != 0)
        {
            return order;
        }
        order = StringComparer.Ordinal.Compare(left.Collection, right.Collection);
        return order != 0 ? order : StringComparer.Ordinal.Compare(left.Id, right.Id);
    }

    internal static int CompareCandidates(GlobalBranchCandidate left, GlobalBranchCandidate right)
    {
        var scoreOrder = right.Score.CompareTo(left.Score);
        return scoreOrder != 0 ? scoreOrder : CompareReferences(left.Reference, right.Reference);
    }

    internal static int CompareScores(SearchScore left, SearchScore right)
    {
        var scoreOrder = right.Score.CompareTo(left.Score);
        return scoreOrder != 0 ? scoreOrder : CompareReferences(left.Reference, right.Reference);
    }

    internal static void SortCandidates(GlobalBranchCandidate[] candidates, ReadExecutionBudget budget)
        => HeapSort(candidates, CompareCandidates, budget);

    internal static void SortScores(SearchScore[] scores, ReadExecutionBudget budget)
        => HeapSort(scores, CompareScores, budget);

    internal static IComparer<SearchScore> WorstFirst(ReadExecutionBudget budget)
        => new BudgetedWorstScoreOrder(budget);

    private static void HeapSort<T>(T[] values, Comparison<T> compare, ReadExecutionBudget budget)
    {
        for (var root = values.Length / 2 - 1; root >= 0; root--)
        {
            SiftDown(values, root, values.Length, compare, budget);
        }
        for (var end = values.Length - 1; end > 0; end--)
        {
            budget.Check();
            (values[0], values[end]) = (values[end], values[0]);
            SiftDown(values, 0, end, compare, budget);
        }
    }

    private static void SiftDown<T>(T[] values, int root, int length, Comparison<T> compare,
        ReadExecutionBudget budget)
    {
        while (root * 2 + 1 < length)
        {
            budget.Check();
            var child = root * 2 + 1;
            if (child + 1 < length && compare(values[child], values[child + 1]) < 0)
            {
                child++;
            }
            if (compare(values[root], values[child]) >= 0)
            {
                return;
            }
            (values[root], values[child]) = (values[child], values[root]);
            root = child;
        }
    }

    private sealed class BudgetedWorstScoreOrder(ReadExecutionBudget budget) : IComparer<SearchScore>
    {
        public int Compare(SearchScore left, SearchScore right)
        {
            budget.Check();
            return -CompareScores(left, right);
        }
    }
}
