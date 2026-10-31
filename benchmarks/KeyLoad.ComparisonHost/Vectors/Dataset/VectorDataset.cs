using System.Globalization;

namespace KeyLoad.ComparisonHost.Vectors;

/// <summary>A vector of the standard vector dataset.</summary>
internal sealed record VectorRecord(string Id, float[] Values);

/// <summary>The standard vector dataset: 32-dimension float32 vectors from a seeded xorshift generator.</summary>
internal static class VectorDataset
{
    internal const int Dimensions = 32;
    internal const int TopK = 10;
    internal const int Queries = 16;
    private const string IdPrefix = "x";
    private const string Format = "D9";
    private const uint Mix = 0x9e3779b9u;
    private const int FractionMask = 0xffffff;
    private const float FractionScale = 8388608f;

    internal static string Id(int number) => IdPrefix + number.ToString(Format, CultureInfo.InvariantCulture);

    internal static VectorRecord Create(int number, int seed) => new(Id(number), Values(number, seed));

    /// <summary>Query vectors come from the same generator, past the end of the dataset.</summary>
    internal static float[] Query(int query, int records, int seed) => Values(records + query, seed);

    internal static float[] Values(int number, int seed)
    {
        var state = unchecked((uint)seed ^ ((uint)number + 1) * Mix);
        state = state == 0 ? 1 : state;
        var vector = new float[Dimensions];
        for (var index = 0; index < vector.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            vector[index] = ((state & FractionMask) / FractionScale) - 1f;
        }
        return vector;
    }
}
