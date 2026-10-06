namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnNeighborSelection
{
    private const int EmptyElementCount = 0;
    private const int FirstElementIndex = 0;
    private const int SingleWorkUnit = 1;
    private const int AdjacentElementOffset = 1;
    private const int FirstOrdinal = 1;

    internal static void Connect(PackedAnnGraph graph, PackedAnnVectors vectors, DistanceMetric metric,
        int dimension, int source, int layer, int candidates,
        PackedAnnBuildScratch scratch, AnnWorkBudget budget)
    {
        var selected = SelectDiverse(source, scratch.Layer.Nodes, scratch.Layer.Scores, candidates,
            graph.Connections, vectors, metric, dimension, scratch, budget);
        graph.ReplaceNeighbors(source, layer, scratch.SelectedNeighbors.AsSpan(EmptyElementCount, selected), budget);
        budget.Charge(selected);
        scratch.SelectedNeighbors.AsSpan(EmptyElementCount, selected).CopyTo(scratch.ConnectionTargets);
        AddReciprocalLinks(graph, vectors, metric, dimension, source, layer,
            scratch.ConnectionTargets, selected, scratch, budget);
    }

    private static int SelectDiverse(int source, int[] nodes, double[] sourceScores, int candidateCount,
        int maximum, PackedAnnVectors vectors, DistanceMetric metric, int dimension,
        PackedAnnBuildScratch scratch, AnnWorkBudget budget)
    {
        if (metric == DistanceMetric.DotProduct)
        {
            return PackedAnnNeighborOrdering.SelectSimple(source, nodes, candidateCount, maximum, scratch, budget);
        }
        var selected = EmptyElementCount;
        for (var index = FirstElementIndex; index < candidateCount && selected < maximum; index++)
        {
            budget.Check();
            var candidate = nodes[index];
            budget.Charge(dimension);
            var similarity = PreparedPackedSimilarity.Create(vectors, candidate, metric);
            if (IsDiverse(sourceScores[index], similarity, scratch.SelectedNeighbors, selected,
                vectors, dimension, budget))
            {
                scratch.SelectedNeighbors[selected++] = candidate;
            }
        }
        return FillPruned(source, nodes, candidateCount, maximum, selected, scratch, budget);
    }

    private static bool IsDiverse(double sourceScore, PreparedPackedSimilarity similarity,
        int[] selectedNodes, int selectedCount, PackedAnnVectors vectors, int dimension, AnnWorkBudget budget)
    {
        for (var index = FirstElementIndex; index < selectedCount; index++)
        {
            budget.Check();
            var pairScore = PackedAnnLayerSearch.Score(vectors, selectedNodes[index], similarity, dimension, budget);
            if (pairScore >= sourceScore)
            {
                return false;
            }
        }
        return true;
    }

    private static int FillPruned(int source, int[] nodes, int candidateCount, int maximum, int selected,
        PackedAnnBuildScratch scratch, AnnWorkBudget budget)
    {
        for (var index = FirstElementIndex; index < candidateCount && selected < maximum; index++)
        {
            budget.Charge(SingleWorkUnit);
            var candidate = nodes[index];
            if (candidate == source || Contains(scratch.SelectedNeighbors, selected, candidate, budget))
            {
                continue;
            }
            scratch.SelectedNeighbors[selected++] = candidate;
        }
        PackedAnnNeighborOrdering.SortSelected(scratch.SelectedNeighbors, selected, budget);
        return selected;
    }

    private static void AddReciprocalLinks(PackedAnnGraph graph, PackedAnnVectors vectors,
        DistanceMetric metric, int dimension, int source, int layer, int[] neighbors, int count,
        PackedAnnBuildScratch scratch, AnnWorkBudget budget)
    {
        for (var index = FirstElementIndex; index < count; index++)
        {
            budget.Check();
            var target = neighbors[index];
            var existing = graph.NeighborCount(target, layer, budget);
            // Ascending insertion means no prior target can already link to this new source.
            var degree = graph.Degree(layer);
            if (existing < degree)
            {
                CopyAndInsertOrdinal(graph, target, layer, existing, source, scratch, budget);
                graph.ReplaceNeighbors(target, layer, scratch.NeighborNodes.AsSpan(EmptyElementCount, existing + AdjacentElementOffset), budget);
                continue;
            }
            CopyAndScoreNeighbors(graph, vectors, metric, dimension, target, source, layer, existing,
                scratch, budget);
            SortPairs(scratch.NeighborNodes, scratch.NeighborScores, existing + AdjacentElementOffset, budget);
            var selected = SelectDiverse(target, scratch.NeighborNodes, scratch.NeighborScores,
                existing + AdjacentElementOffset, degree, vectors, metric, dimension, scratch, budget);
            graph.ReplaceNeighbors(target, layer, scratch.SelectedNeighbors.AsSpan(EmptyElementCount, selected), budget);
        }
    }

    private static void CopyAndInsertOrdinal(PackedAnnGraph graph, int target, int layer, int existing,
        int source, PackedAnnBuildScratch scratch, AnnWorkBudget budget)
    {
        for (var index = FirstElementIndex; index < existing; index++)
        {
            budget.Charge(SingleWorkUnit);
            scratch.NeighborNodes[index] = graph.Neighbor(target, layer, index);
        }
        var insertion = existing;
        while (insertion > EmptyElementCount)
        {
            budget.Charge(SingleWorkUnit);
            if (scratch.NeighborNodes[insertion - AdjacentElementOffset] < source)
            {
                break;
            }
            scratch.NeighborNodes[insertion] = scratch.NeighborNodes[insertion - AdjacentElementOffset];
            insertion--;
        }
        scratch.NeighborNodes[insertion] = source;
    }

    private static void CopyAndScoreNeighbors(PackedAnnGraph graph, PackedAnnVectors vectors,
        DistanceMetric metric, int dimension, int node, int added, int layer, int existing,
        PackedAnnBuildScratch scratch, AnnWorkBudget budget)
    {
        budget.Charge(dimension);
        var similarity = PreparedPackedSimilarity.Create(vectors, node, metric);
        for (var index = FirstElementIndex; index < existing; index++)
        {
            budget.Charge(SingleWorkUnit);
            var neighbor = graph.Neighbor(node, layer, index);
            scratch.NeighborNodes[index] = neighbor;
            scratch.NeighborScores[index] = PackedAnnLayerSearch.Score(vectors, neighbor, similarity, dimension, budget);
        }
        scratch.NeighborNodes[existing] = added;
        scratch.NeighborScores[existing] = PackedAnnLayerSearch.Score(vectors, added, similarity, dimension, budget);
    }

    private static void SortPairs(int[] nodes, double[] scores, int count, AnnWorkBudget budget)
    {
        for (var index = FirstOrdinal; index < count; index++)
        {
            var node = nodes[index];
            var score = scores[index];
            var cursor = index;
            while (cursor > EmptyElementCount)
            {
                budget.Charge(SingleWorkUnit);
                if (!PackedAnnLayerSearch.Better(score, node, scores[cursor - AdjacentElementOffset], nodes[cursor - AdjacentElementOffset], budget))
                {
                    break;
                }
                nodes[cursor] = nodes[cursor - AdjacentElementOffset];
                scores[cursor] = scores[cursor - AdjacentElementOffset];
                cursor--;
            }
            nodes[cursor] = node;
            scores[cursor] = score;
        }
    }

    private static bool Contains(int[] values, int count, int candidate, AnnWorkBudget budget)
    {
        for (var index = FirstElementIndex; index < count; index++)
        {
            budget.Charge(SingleWorkUnit);
            if (values[index] == candidate)
            {
                return true;
            }
        }
        return false;
    }
}
