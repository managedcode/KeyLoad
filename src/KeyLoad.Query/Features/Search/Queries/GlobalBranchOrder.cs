using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class GlobalBranchOrder
{
    private const int EqualOrder = 0;
    private const int HeapChildCount = 2;
    private const int AdjacentElementOffset = 1;
    private const int EmptyElementCount = 0;
    private const int FirstElementIndex = 0;

    internal static int CompareReferences(EntityRef left, EntityRef right)
    {
        var order = StringComparer.Ordinal.Compare(left.Partition.TenantId, right.Partition.TenantId);
        if (order != EqualOrder)
        {
            return order;
        }
        order = StringComparer.Ordinal.Compare(left.Partition.DatabaseId, right.Partition.DatabaseId);
        if (order != EqualOrder)
        {
            return order;
        }
        order = StringComparer.Ordinal.Compare(left.Partition.TransactionDomainId, right.Partition.TransactionDomainId);
        if (order != EqualOrder)
        {
            return order;
        }
        order = StringComparer.Ordinal.Compare(left.Partition.PartitionKey, right.Partition.PartitionKey);
        if (order != EqualOrder)
        {
            return order;
        }
        order = StringComparer.Ordinal.Compare(left.Collection, right.Collection);
        return order != EqualOrder ? order : StringComparer.Ordinal.Compare(left.Id, right.Id);
    }

    internal static int CompareCandidates(GlobalBranchCandidate left, GlobalBranchCandidate right)
    {
        var scoreOrder = right.Score.CompareTo(left.Score);
        return scoreOrder != EqualOrder ? scoreOrder : CompareReferences(left.Reference, right.Reference);
    }

    internal static int CompareScores(SearchScore left, SearchScore right)
    {
        var scoreOrder = right.Score.CompareTo(left.Score);
        return scoreOrder != EqualOrder ? scoreOrder : CompareReferences(left.Reference, right.Reference);
    }

    internal static void SortCandidates(GlobalBranchCandidate[] candidates, ReadExecutionBudget budget)
        => HeapSort(candidates, CompareCandidates, budget);

    internal static void SortScores(SearchScore[] scores, ReadExecutionBudget budget)
        => HeapSort(scores, CompareScores, budget);

    internal static IComparer<SearchScore> WorstFirst(ReadExecutionBudget budget)
        => new BudgetedWorstScoreOrder(budget);

    private static void HeapSort<T>(T[] values, Comparison<T> compare, ReadExecutionBudget budget)
    {
        for (var root = values.Length / HeapChildCount - AdjacentElementOffset; root >= EmptyElementCount; root--)
        {
            SiftDown(values, root, values.Length, compare, budget);
        }
        for (var end = values.Length - AdjacentElementOffset; end > EmptyElementCount; end--)
        {
            budget.Check();
            (values[FirstElementIndex], values[end]) = (values[end], values[FirstElementIndex]);
            SiftDown(values, FirstElementIndex, end, compare, budget);
        }
    }

    private static void SiftDown<T>(T[] values, int root, int length, Comparison<T> compare,
        ReadExecutionBudget budget)
    {
        while (root * HeapChildCount + AdjacentElementOffset < length)
        {
            budget.Check();
            var child = root * HeapChildCount + AdjacentElementOffset;
            if (child + AdjacentElementOffset < length && compare(values[child], values[child + AdjacentElementOffset]) < EmptyElementCount)
            {
                child++;
            }
            if (compare(values[root], values[child]) >= EmptyElementCount)
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
