namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnNeighborOrdering
{
    internal static int SelectSimple(int source, int[] nodes, int candidateCount, int maximum,
        PackedAnnBuildScratch scratch, AnnWorkBudget budget)
    {
        var selected = 0;
        for (var index = 0; index < candidateCount && selected < maximum; index++)
        {
            budget.Charge(1);
            var candidate = nodes[index];
            if (candidate == source)
            {
                continue;
            }
            budget.Charge(1);
            scratch.SelectedNeighbors[selected++] = candidate;
        }
        SortSelected(scratch.SelectedNeighbors, selected, budget);
        return selected;
    }

    internal static void SortSelected(int[] nodes, int count, AnnWorkBudget budget)
    {
        for (var index = 1; index < count; index++)
        {
            var node = nodes[index];
            var cursor = index;
            while (cursor > 0)
            {
                budget.Charge(1);
                if (node >= nodes[cursor - 1])
                {
                    break;
                }
                nodes[cursor] = nodes[cursor - 1];
                cursor--;
            }
            nodes[cursor] = node;
        }
    }

}
