using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal static class VectorExactOracle
{
    internal static IReadOnlyList<IReadOnlyList<VectorNeighbor>> Compute(VectorComparisonCorpus corpus,
        IReadOnlyList<ReadOnlyMemory<float>> queries, IOptions<NativeComparisonExecutionOptions> executionOptions,
        CancellationToken cancellationToken)
    {
        var cancellationMask = NativeComparisonExecutionOptions.Require(executionOptions).Value.VectorCancellationCheckMask;
        ArgumentNullException.ThrowIfNull(queries);
        if (queries.Count == VectorExactOracleValues.FirstIndex || queries.Count > corpus.Profile.QueryVectorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(queries));
        }

        var norms = queries.Select(query => Norm(query.Span, corpus.Profile.Dimensions)).ToArray();
        var heaps = queries.Select(_ => new PriorityQueue<int, (double Similarity, int ReverseNumber)>()).ToArray();
        Span<float> vector = stackalloc float[corpus.Profile.Dimensions];
        for (var number = VectorExactOracleValues.FirstIndex; number < corpus.Profile.RecordCount; number++)
        {
            if ((number & cancellationMask) == VectorExactOracleValues.FirstIndex)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (!corpus.Eligible(number))
            {
                continue;
            }

            corpus.FillEmbedding(number, vector);
            var norm = Norm(vector, vector.Length);
            for (var index = VectorExactOracleValues.FirstIndex; index < queries.Count; index++)
            {
                var similarity = Dot(vector, queries[index].Span) / (norm * norms[index]);
                Retain(heaps[index], number, similarity, corpus.Profile.TopK);
            }
        }
        return heaps.Select(heap => (IReadOnlyList<VectorNeighbor>)heap.UnorderedItems
            .Select(item => new VectorNeighbor(VectorExactOracleValues.VectorIdPrefix + item.Element.ToString(VectorProfileTokens.NumberFormat, System.Globalization.CultureInfo.InvariantCulture),
                VectorExactOracleValues.ExactRecall - item.Priority.Similarity))
            .OrderBy(item => item.Distance).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray()).ToArray();
    }

    private static double Norm(ReadOnlySpan<float> vector, int dimensions)
    {
        if (vector.Length != dimensions)
        {
            throw new ArgumentException(VectorExactOracleValues.TheQueryDimensionDiffersFromThe, nameof(vector));
        }
        var sum = Dot(vector, vector);
        if (!double.IsFinite(sum) || sum <= VectorExactOracleValues.FirstIndex)
        {
            throw new ArgumentException(VectorExactOracleValues.TheQueryMustHaveAFinite, nameof(vector));
        }
        return Math.Sqrt(sum);
    }

    private static double Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var sum = VectorExactOracleValues.ZeroObservation;
        for (var index = VectorExactOracleValues.FirstIndex; index < left.Length; index++)
        {
            sum += (double)left[index] * right[index];
        }
        return sum;
    }

    private static void Retain(PriorityQueue<int, (double Similarity, int ReverseNumber)> heap,
        int number, double similarity, int count)
    {
        var priority = (similarity, -number);
        if (heap.Count < count)
        {
            heap.Enqueue(number, priority);
        }
        else if (heap.TryPeek(out _, out var worst) && priority.CompareTo(worst) > VectorExactOracleValues.FirstIndex)
        {
            heap.Dequeue();
            heap.Enqueue(number, priority);
        }
    }
}
