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
