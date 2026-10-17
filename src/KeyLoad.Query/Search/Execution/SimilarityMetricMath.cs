using System.Numerics;

namespace KeyLoad.Query.Features.Search;

internal static class SimilarityMetricMath
{
    private const int ZeroMagnitude = 0;
    private const int FirstCoordinateIndex = 0;
    private const int PairUnrollWidth = 2;

    private const string InvalidMetric = "The vector metric is invalid.";

    internal static double Norm(ReadOnlySpan<float> values)
    {
        double result = ZeroMagnitude;
        var index = FirstCoordinateIndex;
        for (; index + Vector<float>.Count <= values.Length; index += Vector<float>.Count)
        {
            Vector.Widen(new Vector<float>(values[index..]), out var first, out var second);
            result += Vector.Dot(first, first) + Vector.Dot(second, second);
        }
        for (; index < values.Length; index++)
        {
            result += (double)values[index] * values[index];
        }
        return result;
    }

    internal static double QueryNorm(ReadOnlySpan<float> query, DistanceMetric metric)
        => metric == DistanceMetric.Cosine ? Norm(query) : ZeroMagnitude;

    internal static double Score(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate,
        DistanceMetric metric, double queryNorm)
    {
        return metric switch
        {
            DistanceMetric.DotProduct => Dot(query, candidate),
            DistanceMetric.Euclidean => -Math.Sqrt(DistanceSquared(query, candidate)),
            DistanceMetric.Cosine => Cosine(query, candidate, queryNorm),
            _ => throw Errors.Fail(ErrorCode.Validation, InvalidMetric)
        };
    }

    internal static void ValidateMetric(DistanceMetric metric)
    {
        if (!Enum.IsDefined(metric))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidMetric);
        }
    }

    private static double Cosine(ReadOnlySpan<float> source, ReadOnlySpan<float> candidate, double queryNorm)
    {
        if (queryNorm == ZeroMagnitude)
        {
            return ZeroMagnitude;
        }
        double dot = ZeroMagnitude, candidateNorm = ZeroMagnitude;
        var index = FirstCoordinateIndex;
        for (; index + Vector<float>.Count <= source.Length; index += Vector<float>.Count)
        {
            Vector.Widen(new Vector<float>(source[index..]), out var left1, out var left2);
            Vector.Widen(new Vector<float>(candidate[index..]), out var right1, out var right2);
            dot += Vector.Dot(left1, right1) + Vector.Dot(left2, right2);
            candidateNorm += Vector.Dot(right1, right1) + Vector.Dot(right2, right2);
        }
        for (; index < source.Length; index++)
        {
            dot += (double)source[index] * candidate[index];
            candidateNorm += (double)candidate[index] * candidate[index];
        }
        return candidateNorm == ZeroMagnitude ? ZeroMagnitude : dot / Math.Sqrt(queryNorm * candidateNorm);
    }

    private static double Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double result = ZeroMagnitude;
        var index = FirstCoordinateIndex;
        for (; index + Vector<float>.Count <= left.Length; index += Vector<float>.Count)
        {
            Vector.Widen(new Vector<float>(left[index..]), out var left1, out var left2);
            Vector.Widen(new Vector<float>(right[index..]), out var right1, out var right2);
            result += Vector.Dot(left1, right1) + Vector.Dot(left2, right2);
        }
        for (; index < left.Length; index++)
        {
            result += (double)left[index] * right[index];
        }
        return result;
    }

    private static double DistanceSquared(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double result = ZeroMagnitude;
        var index = FirstCoordinateIndex;
        for (; index + Vector<float>.Count <= left.Length; index += Vector<float>.Count)
        {
            Vector.Widen(new Vector<float>(left[index..]), out var left1, out var left2);
            Vector.Widen(new Vector<float>(right[index..]), out var right1, out var right2);
            var delta1 = left1 - right1;
            var delta2 = left2 - right2;
            result += Vector.Dot(delta1, delta1) + Vector.Dot(delta2, delta2);
        }
        for (; index < left.Length; index++)
        {
            result += Math.Pow((double)left[index] - right[index], PairUnrollWidth);
        }
        return result;
    }
}

internal readonly struct PreparedPackedSimilarity : IPackedAnnSimilarity
{
    private const int MinimumPositiveCount = 1;

    private const string InvalidVector = "Vector dimensions or values are invalid.";
    private const int MaxDimension = 4_096;
    private readonly ReadOnlyMemory<float> query;
    private readonly DistanceMetric metric;
    private readonly double queryNorm;

    private PreparedPackedSimilarity(ReadOnlyMemory<float> query, DistanceMetric metric)
    {
        this.query = query;
        this.metric = metric;
        queryNorm = SimilarityMetricMath.QueryNorm(query.Span, metric);
    }

    internal static PreparedPackedSimilarity Create(PackedAnnVectors vectors, int ordinal, DistanceMetric metric)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        SimilarityMetricMath.ValidateMetric(metric);
        return new(vectors.Memory(ordinal), metric);
    }

    internal double Score(PackedAnnVectors vectors, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        SimilarityMetricMath.ValidateMetric(metric);
        if (query.Length is < MinimumPositiveCount or > MaxDimension || vectors.Dimension != query.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
        return SimilarityMetricMath.Score(query.Span, vectors.Span(ordinal), metric, queryNorm);
    }

    double IPackedAnnSimilarity.ScorePacked(PackedAnnVectors vectors, int ordinal)
        => Score(vectors, ordinal);
}
