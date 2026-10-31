namespace KeyLoad.Query.Search;

internal readonly struct PreparedPackedSimilarity : IPackedAnnSimilarity
{
    private const string InvalidVector = "Vector dimensions or values are invalid.";
    private const int MaxDimension = 4_096;
    private readonly ReadOnlyMemory<float> query;
    private readonly DistanceMetric metric;
    private readonly double queryNorm;

    private PreparedPackedSimilarity(ReadOnlyMemory<float> query, DistanceMetric metric, double queryNorm)
    {
        this.query = query;
        this.metric = metric;
        this.queryNorm = queryNorm;
    }

    internal static PreparedPackedSimilarity Create(PackedAnnVectors vectors, int ordinal, DistanceMetric metric)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        SimilarityMetricMath.ValidateMetric(metric);
        return new(vectors.Memory(ordinal), metric, metric == DistanceMetric.Cosine ? vectors.SquaredNorm(ordinal) : 0);
    }

    internal double Score(PackedAnnVectors vectors, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        SimilarityMetricMath.ValidateMetric(metric);
        if (query.Length is < 1 or > MaxDimension || vectors.Dimension != query.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
        return metric == DistanceMetric.Cosine
            ? SimilarityMetricMath.CosineWithNorms(query.Span, vectors.Span(ordinal), queryNorm, vectors.SquaredNorm(ordinal))
            : SimilarityMetricMath.Score(query.Span, vectors.Span(ordinal), metric, queryNorm);
    }

    double IPackedAnnSimilarity.ScorePacked(PackedAnnVectors vectors, int ordinal)
        => Score(vectors, ordinal);
}
