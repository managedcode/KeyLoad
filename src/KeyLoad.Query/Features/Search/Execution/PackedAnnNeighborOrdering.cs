namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnNeighborOrdering
{
    private const int EmptyElementCount = 0;
    private const int FirstElementIndex = 0;
    private const int SingleWorkUnit = 1;
    private const int FirstOrdinal = 1;
    private const int AdjacentElementOffset = 1;

    internal static int SelectSimple(int source, int[] nodes, int candidateCount, int maximum,
        PackedAnnBuildScratch scratch, AnnWorkBudget budget)
    {
        var selected = EmptyElementCount;
        for (var index = FirstElementIndex; index < candidateCount && selected < maximum; index++)
        {
            budget.Charge(SingleWorkUnit);
            var candidate = nodes[index];
            if (candidate == source)
            {
                continue;
            }
            budget.Charge(SingleWorkUnit);
            scratch.SelectedNeighbors[selected++] = candidate;
        }
        SortSelected(scratch.SelectedNeighbors, selected, budget);
        return selected;
    }

    internal static void SortSelected(int[] nodes, int count, AnnWorkBudget budget)
    {
        for (var index = FirstOrdinal; index < count; index++)
        {
            var node = nodes[index];
            var cursor = index;
            while (cursor > EmptyElementCount)
            {
                budget.Charge(SingleWorkUnit);
                if (node >= nodes[cursor - AdjacentElementOffset])
                {
                    break;
                }
                nodes[cursor] = nodes[cursor - AdjacentElementOffset];
                cursor--;
            }
            nodes[cursor] = node;
        }
    }

}
