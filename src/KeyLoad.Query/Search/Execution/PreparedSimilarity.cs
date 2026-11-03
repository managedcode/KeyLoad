using System.Numerics;

namespace KeyLoad.Query.Search;

internal sealed class PreparedSimilarity : IPackedAnnSimilarity
{
    private const int MinimumPositiveCount = 1;
    private const int FirstElementIndex = 0;

    private const string InvalidVector = "Vector dimensions or values are invalid.";
    private const int MaxDimension = 4_096;
    private readonly ReadOnlyMemory<float> query;
    private readonly DistanceMetric metric;
    private readonly double queryNorm;

    private PreparedSimilarity(ReadOnlyMemory<float> query, DistanceMetric metric, double queryNorm)
    {
        this.query = query;
        this.metric = metric;
        this.queryNorm = queryNorm;
    }

    public static PreparedSimilarity Create(ReadOnlyMemory<float> query, DistanceMetric metric)
    {
        Validate(query.Span);
        SimilarityMetricMath.ValidateMetric(metric);
        return new(query, metric, SimilarityMetricMath.QueryNorm(query.Span, metric));
    }

    internal static PreparedSimilarity CreatePacked(PackedAnnVectors vectors, int ordinal, DistanceMetric metric)
    {
        ArgumentNullException.ThrowIfNull(vectors) /* KLD0041: BCL null validation accepts the vectors PackedAnnVectors reference through its object parameter. */;
        SimilarityMetricMath.ValidateMetric(metric);
        return new(vectors.Memory(ordinal), metric, metric == DistanceMetric.Cosine ? vectors.SquaredNorm(ordinal) : 0);
    }

    public double Score(ReadOnlyMemory<float> candidate) => ScoreBorrowed(candidate.Span);

    internal double ScoreBorrowed(ReadOnlySpan<float> candidate)
    {
        Validate(candidate);
        if (candidate.Length != query.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
        return ScoreCore(candidate);
    }

    internal double ScorePacked(PackedAnnVectors vectors, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(vectors) /* KLD0041: BCL null validation accepts the vectors PackedAnnVectors reference through its object parameter. */;
        if (vectors.Dimension != query.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
        var candidate = vectors.Memory(ordinal);
        return metric == DistanceMetric.Cosine
            ? SimilarityMetricMath.CosineWithNorms(query.Span, candidate.Span, queryNorm, vectors.SquaredNorm(ordinal))
            : ScoreCore(candidate.Span);
    }

    double IPackedAnnSimilarity.ScorePacked(PackedAnnVectors vectors, int ordinal)
        => ScorePacked(vectors, ordinal);

    private double ScoreCore(ReadOnlySpan<float> candidate)
        => SimilarityMetricMath.Score(query.Span, candidate, metric, queryNorm);

    private static void Validate(ReadOnlySpan<float> values)
    {
        if (values.Length is < MinimumPositiveCount or > MaxDimension)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
        SimdRuntime.RequireHardwareSupport();
        var index = FirstElementIndex;
        var infinity = new Vector<float>(float.PositiveInfinity);
        for (; index + Vector<float>.Count <= values.Length; index += Vector<float>.Count)
        {
            var block = new Vector<float>(values[index..]);
            if (!Vector.LessThanAll(Vector.Abs(block), infinity))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidVector);
            }
        }
        for (; index < values.Length; index++)
        {
            if (!float.IsFinite(values[index]))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidVector);
            }
        }
    }
}
