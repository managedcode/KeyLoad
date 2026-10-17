namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnLayerSearch
{
    private const int FirstElementIndex = 0;
    private const int FirstOrdinal = 1;
    private const int CandidateComparisonWork = 2;
    private const int EmptyElementCount = 0;
    private const int AdjacentElementOffset = 1;
    private const int SingleWorkUnit = 1;

    internal static double Score<TSimilarity>(PackedAnnVectors vectors, int node, in TSimilarity similarity,
        int dimension, AnnWorkBudget budget) where TSimilarity : IPackedAnnSimilarity
    {
        budget.ChargeDistance(dimension);
        return similarity.ScorePacked(vectors, node);
    }

    internal static int Greedy<TSimilarity>(PackedAnnGraph graph, PackedAnnVectors vectors,
        in TSimilarity similarity, int start, int layer, int dimension, AnnWorkBudget budget)
        where TSimilarity : IPackedAnnSimilarity
    {
        var current = start;
        var currentScore = Score(vectors, current, in similarity, dimension, budget);
        while (true)
        {
            var next = current;
            var nextScore = currentScore;
            var count = graph.NeighborCount(current, layer, budget);
            for (var offset = FirstElementIndex; offset < count; offset++)
            {
                budget.ChargeEdge();
                var candidate = graph.Neighbor(current, layer, offset);
                var score = Score(vectors, candidate, in similarity, dimension, budget);
                if (Better(score, candidate, nextScore, next, budget))
                {
                    next = candidate;
                    nextScore = score;
                }
            }
            if (next == current)
            {
                return current;
            }
            current = next;
            currentScore = nextScore;
        }
    }

    internal static int SearchLayer<TSimilarity>(PackedAnnGraph graph, PackedAnnVectors vectors,
        in TSimilarity similarity, int entry, int layer, int ef, int dimension,
        PackedAnnLayerBuffers buffers, AnnWorkBudget budget)
        where TSimilarity : IPackedAnnSimilarity
    {
        buffers.BeginVisitPass(budget);
        var count = FirstOrdinal;
        budget.Charge(CandidateComparisonWork);
        buffers.Nodes[FirstElementIndex] = entry;
        buffers.Scores[FirstElementIndex] = Score(vectors, entry, in similarity, dimension, budget);
        PackedAnnCandidateHeaps.AddRetained(buffers, EmptyElementCount, budget);
        buffers.MarkVisited(entry);
        while (true)
        {
            budget.Check();
            var best = PackedAnnCandidateHeaps.PopBest(buffers, budget);
            if (best < EmptyElementCount)
            {
                break;
            }
            count = VisitNeighbors(graph, vectors, in similarity, layer, dimension,
                buffers, budget, best, count, ef);
        }
        SortCandidates(buffers, count, budget);
        return count;
    }

    private static int VisitNeighbors<TSimilarity>(PackedAnnGraph graph, PackedAnnVectors vectors,
        in TSimilarity similarity, int layer, int dimension, PackedAnnLayerBuffers buffers,
        AnnWorkBudget budget, int slot, int count, int ef)
        where TSimilarity : IPackedAnnSimilarity
    {
        var node = buffers.Nodes[slot];
        var neighborCount = graph.NeighborCount(node, layer, budget);
        for (var offset = FirstElementIndex; offset < neighborCount; offset++)
        {
            budget.ChargeEdge();
            var candidate = graph.Neighbor(node, layer, offset);
            if (buffers.IsVisited(candidate))
            {
                continue;
            }
            buffers.MarkVisited(candidate);
            var score = Score(vectors, candidate, in similarity, dimension, budget);
            count = AddCandidate(buffers, count, ef, candidate, score, budget);
        }
        return count;
    }

    private static int AddCandidate(PackedAnnLayerBuffers buffers, int count, int capacity,
        int node, double score, AnnWorkBudget budget)
    {
        if (count < capacity)
        {
            budget.Charge(CandidateComparisonWork);
            buffers.Nodes[count] = node;
            buffers.Scores[count] = score;
            PackedAnnCandidateHeaps.AddRetained(buffers, count, budget);
            return count + AdjacentElementOffset;
        }
        var worst = buffers.WorstHeapSlots[FirstElementIndex];
        budget.Charge(SingleWorkUnit);
        if (Better(score, node, buffers.Scores[worst], buffers.Nodes[worst], budget))
        {
            PackedAnnCandidateHeaps.RemoveBestIfPresent(buffers, worst, budget);
            budget.Charge(CandidateComparisonWork);
            buffers.Nodes[worst] = node;
            buffers.Scores[worst] = score;
            PackedAnnCandidateHeaps.ReplaceWorstRoot(buffers, budget);
            PackedAnnCandidateHeaps.AddBest(buffers, worst, budget);
        }
        return count;
    }

    private static void SortCandidates(PackedAnnLayerBuffers buffers, int count, AnnWorkBudget budget)
    {
        for (var index = FirstOrdinal; index < count; index++)
        {
            var node = buffers.Nodes[index];
            var score = buffers.Scores[index];
            var cursor = index;
            while (cursor > EmptyElementCount)
            {
                budget.Charge(SingleWorkUnit);
                if (!Better(score, node, buffers.Scores[cursor - AdjacentElementOffset], buffers.Nodes[cursor - AdjacentElementOffset], budget))
                {
                    break;
                }
                buffers.Nodes[cursor] = buffers.Nodes[cursor - AdjacentElementOffset];
                buffers.Scores[cursor] = buffers.Scores[cursor - AdjacentElementOffset];
                cursor--;
            }
            buffers.Nodes[cursor] = node;
            buffers.Scores[cursor] = score;
        }
    }

    internal static bool Better(double leftScore, int leftNode, double rightScore, int rightNode,
        AnnWorkBudget budget)
    {
        if (leftScore != rightScore)
        {
            return leftScore > rightScore;
        }
        budget.Charge(SingleWorkUnit);
        return leftNode < rightNode;
    }

}
