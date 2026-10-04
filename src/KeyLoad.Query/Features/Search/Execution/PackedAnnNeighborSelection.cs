namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnNeighborSelection
{
    internal static void Connect(PackedAnnGraph graph, PackedAnnVectors vectors, DistanceMetric metric,
        int dimension, int source, int layer, int candidates,
        PackedAnnBuildScratch scratch, AnnWorkBudget budget)
    {
        var selected = SelectDiverse(source, scratch.Layer.Nodes, scratch.Layer.Scores, candidates,
            graph.Connections, vectors, metric, dimension, scratch, budget);
        graph.ReplaceNeighbors(source, layer, scratch.SelectedNeighbors.AsSpan(0, selected), budget);
        budget.Charge(selected);
        scratch.SelectedNeighbors.AsSpan(0, selected).CopyTo(scratch.ConnectionTargets);
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
        var selected = 0;
        for (var index = 0; index < candidateCount && selected < maximum; index++)
        {
            budget.Check();
            var candidate = nodes[index];
            budget.Charge(dimension);
            var similarity = PreparedSimilarity.CreatePacked(vectors, candidate, metric);
            if (IsDiverse(sourceScores[index], similarity, scratch.SelectedNeighbors, selected,
                vectors, dimension, budget))
            {
                scratch.SelectedNeighbors[selected++] = candidate;
            }
        }
        return FillPruned(source, nodes, candidateCount, maximum, selected, scratch, budget);
    }

    private static bool IsDiverse(double sourceScore, PreparedSimilarity similarity,
        int[] selectedNodes, int selectedCount, PackedAnnVectors vectors, int dimension, AnnWorkBudget budget)
    {
        for (var index = 0; index < selectedCount; index++)
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
        for (var index = 0; index < candidateCount && selected < maximum; index++)
        {
            budget.Check();
            budget.Charge(1);
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
        for (var index = 0; index < count; index++)
        {
            budget.Check();
            var target = neighbors[index];
            var existing = graph.NeighborCount(target, layer, budget);
            if (ContainsNeighbors(graph, target, layer, existing, source, budget))
            {
                continue;
            }
            var degree = graph.Degree(layer);
            if (existing < degree)
            {
                CopyAndInsertOrdinal(graph, target, layer, existing, source, scratch, budget);
                graph.ReplaceNeighbors(target, layer, scratch.NeighborNodes.AsSpan(0, existing + 1), budget);
                continue;
            }
            CopyAndScoreNeighbors(graph, vectors, metric, dimension, target, source, layer, existing,
                scratch, budget);
            SortPairs(scratch.NeighborNodes, scratch.NeighborScores, existing + 1, budget);
            var selected = SelectDiverse(target, scratch.NeighborNodes, scratch.NeighborScores,
                existing + 1, degree, vectors, metric, dimension, scratch, budget);
            graph.ReplaceNeighbors(target, layer, scratch.SelectedNeighbors.AsSpan(0, selected), budget);
        }
    }

    private static void CopyAndInsertOrdinal(PackedAnnGraph graph, int target, int layer, int existing,
        int source, PackedAnnBuildScratch scratch, AnnWorkBudget budget)
    {
        for (var index = 0; index < existing; index++)
        {
            budget.Charge(1);
            scratch.NeighborNodes[index] = graph.Neighbor(target, layer, index);
        }
        var insertion = existing;
        while (insertion > 0)
        {
            budget.Charge(1);
            if (scratch.NeighborNodes[insertion - 1] < source)
            {
                break;
            }
            scratch.NeighborNodes[insertion] = scratch.NeighborNodes[insertion - 1];
            insertion--;
        }
        scratch.NeighborNodes[insertion] = source;
    }

    private static bool ContainsNeighbors(PackedAnnGraph graph, int node, int layer, int count, int value,
        AnnWorkBudget budget)
    {
        for (var index = 0; index < count; index++)
        {
            budget.Charge(1);
            if (graph.Neighbor(node, layer, index) == value)
            {
                return true;
            }
        }
        return false;
    }

    private static void CopyAndScoreNeighbors(PackedAnnGraph graph, PackedAnnVectors vectors,
        DistanceMetric metric, int dimension, int node, int added, int layer, int existing,
        PackedAnnBuildScratch scratch, AnnWorkBudget budget)
    {
        budget.Charge(dimension);
        var similarity = PreparedSimilarity.CreatePacked(vectors, node, metric);
        for (var index = 0; index < existing; index++)
        {
            budget.Charge(1);
            var neighbor = graph.Neighbor(node, layer, index);
            scratch.NeighborNodes[index] = neighbor;
            scratch.NeighborScores[index] = PackedAnnLayerSearch.Score(vectors, neighbor, similarity, dimension, budget);
        }
        scratch.NeighborNodes[existing] = added;
        scratch.NeighborScores[existing] = PackedAnnLayerSearch.Score(vectors, added, similarity, dimension, budget);
    }

    private static void SortPairs(int[] nodes, double[] scores, int count, AnnWorkBudget budget)
    {
        for (var index = 1; index < count; index++)
        {
            var node = nodes[index];
            var score = scores[index];
            var cursor = index;
            while (cursor > 0)
            {
                budget.Charge(1);
                if (!PackedAnnLayerSearch.Better(score, node, scores[cursor - 1], nodes[cursor - 1], budget))
                {
                    break;
                }
                nodes[cursor] = nodes[cursor - 1];
                scores[cursor] = scores[cursor - 1];
                cursor--;
            }
            nodes[cursor] = node;
            scores[cursor] = score;
        }
    }

    private static bool Contains(int[] values, int count, int candidate, AnnWorkBudget budget)
    {
        for (var index = 0; index < count; index++)
        {
            budget.Charge(1);
            if (values[index] == candidate)
            {
                return true;
            }
        }
        return false;
    }
}
