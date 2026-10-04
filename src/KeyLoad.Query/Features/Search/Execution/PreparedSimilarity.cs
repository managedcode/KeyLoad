using System.Numerics;

namespace KeyLoad.Query.Features.Search;

internal sealed class PreparedSimilarity
{
    private const string InvalidVector = "Vector dimensions or values are invalid.";
    private const string InvalidMetric = "The vector metric is invalid.";
    private const int MaxDimension = 4_096;
    private readonly ReadOnlyMemory<float> query;
    private readonly DistanceMetric metric;
    private readonly double queryNorm;

    private PreparedSimilarity(ReadOnlyMemory<float> query, DistanceMetric metric)
    {
        this.query = query;
        this.metric = metric;
        queryNorm = metric == DistanceMetric.Cosine ? Norm(query.Span) : 0;
    }

    public static PreparedSimilarity Create(ReadOnlyMemory<float> query, DistanceMetric metric)
    {
        Validate(query.Span);
        if (!Enum.IsDefined(metric))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidMetric);
        }
        return new(query, metric);
    }

    public double Score(ReadOnlyMemory<float> candidate)
    {
        Validate(candidate.Span);
        if (candidate.Length != query.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVector);
        }
        return metric switch
        {
            DistanceMetric.DotProduct => Dot(query.Span, candidate.Span),
            DistanceMetric.Euclidean => -Math.Sqrt(DistanceSquared(query.Span, candidate.Span)),
            DistanceMetric.Cosine => Cosine(candidate.Span),
            _ => throw Errors.Fail(ErrorCode.Validation, InvalidMetric)
        };
    }

    private double Cosine(ReadOnlySpan<float> candidate)
    {
        if (queryNorm == 0)
        {
            return 0;
        }
        double dot = 0, candidateNorm = 0;
        var source = query.Span;
        var index = 0;
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
        return candidateNorm == 0 ? 0 : dot / Math.Sqrt(queryNorm * candidateNorm);
    }

    private static double Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double result = 0;
        var index = 0;
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

    private static double Norm(ReadOnlySpan<float> values)
    {
        double result = 0;
        var index = 0;
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

    private static double DistanceSquared(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double result = 0;
        var index = 0;
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
            result += Math.Pow((double)left[index] - right[index], 2);
        }
        return result;
    }

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
