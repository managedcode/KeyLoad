using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>Scalar cosine oracle for the deterministic control corpus.</summary>
internal static class BenchmarkCosineOracle
{
    internal static double Score(ImmutableArray<float> left, ImmutableArray<float> right)
    {
        const int ZeroAccumulator = 0;
        const int FirstElementIndex = 0;

        double dot = ZeroAccumulator, a = ZeroAccumulator, b = ZeroAccumulator;
        for (var i = FirstElementIndex; i < left.Length; i++)
        { dot += (double)left[i] * right[i]; a += (double)left[i] * left[i]; b += (double)right[i] * right[i]; }
        return dot / Math.Sqrt(a * b);
    }

}
