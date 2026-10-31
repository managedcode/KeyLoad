namespace KeyLoad.ComparisonHost.Vectors;

/// <summary>The exact cosine top-k of every query vector, computed independently by a scalar scan of the dataset.</summary>
internal static class VectorOracle
{
    internal static HashSet<string>[] Compute(int records, int seed)
    {
        var queries = Enumerable.Range(0, VectorDataset.Queries).Select(query => Normalize(VectorDataset.Query(query, records, seed))).ToArray();
        var best = queries.Select(_ => new PriorityQueue<int, double>()).ToArray();
        for (var number = 0; number < records; number++)
        {
            var vector = Normalize(VectorDataset.Values(number, seed));
            for (var query = 0; query < queries.Length; query++)
            {
                Offer(best[query], number, Dot(queries[query], vector));
            }
        }
        return [.. best.Select(heap => heap.UnorderedItems.Select(item => VectorDataset.Id(item.Element)).ToHashSet(StringComparer.Ordinal))];
    }

    private static void Offer(PriorityQueue<int, double> heap, int number, double similarity)
    {
        if (heap.Count < VectorDataset.TopK)
        {
            heap.Enqueue(number, similarity);
        }
        else if (heap.TryPeek(out _, out var lowest) && similarity > lowest)
        {
            heap.DequeueEnqueue(number, similarity);
        }
    }

    private static double Dot(float[] left, float[] right)
    {
        var sum = 0d;
        for (var index = 0; index < left.Length; index++)
        {
            sum += left[index] * (double)right[index];
        }
        return sum;
    }

    private static float[] Normalize(float[] vector)
    {
        var length = Math.Sqrt(Dot(vector, vector));
        return [.. vector.Select(value => (float)(value / length))];
    }
}
