using System.Numerics;

namespace KeyLoad.Query.Features.Search;

internal sealed class PreparedSimilarity : IPackedAnnSimilarity
{
    private const string InvalidVector = "Vector dimensions or values are invalid.";
    private const int MaxDimension = 4_096;
    private readonly ReadOnlyMemory<float> query;
    private readonly DistanceMetric metric;
    private readonly double queryNorm;

    private PreparedSimilarity(ReadOnlyMemory<float> query, DistanceMetric metric)
    {
        this.query = query;
        this.metric = metric;
        queryNorm = SimilarityMetricMath.QueryNorm(query.Span, metric);
    }

    public static PreparedSimilarity Create(ReadOnlyMemory<float> query, DistanceMetric metric)
    {
        Validate(query.Span);
        SimilarityMetricMath.ValidateMetric(metric);
        return new(query, metric);
    }

    internal static PreparedSimilarity CreatePacked(PackedAnnVectors vectors, int ordinal, DistanceMetric metric)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        SimilarityMetricMath.ValidateMetric(metric);
        return new(vectors.Memory(ordinal), metric);
    }

    public double Score(ReadOnlyMemory<float> candidate)
    {
        Validate(candidate.Span);
        if (candidate.Length != query.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
        return ScoreCore(candidate);
    }

    internal double ScorePacked(PackedAnnVectors vectors, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        if (vectors.Dimension != query.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
        var candidate = vectors.Memory(ordinal);
        return ScoreCore(candidate);
    }

    double IPackedAnnSimilarity.ScorePacked(PackedAnnVectors vectors, int ordinal)
        => ScorePacked(vectors, ordinal);

    private double ScoreCore(ReadOnlyMemory<float> candidate)
        => SimilarityMetricMath.Score(query.Span, candidate.Span, metric, queryNorm);

    private static void Validate(ReadOnlySpan<float> values)
    {
        if (values.Length is < 1 or > MaxDimension)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
        var index = 0;
        var infinity = new Vector<float>(float.PositiveInfinity);
        for (; Vector.IsHardwareAccelerated && index + Vector<float>.Count <= values.Length; index += Vector<float>.Count)
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
