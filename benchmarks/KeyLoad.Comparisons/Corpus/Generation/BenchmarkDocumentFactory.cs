using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons;

internal static class BenchmarkDocumentFactory
{
    private const string DocumentNumberFormat = "D9";

    internal static BenchmarkDocument Create(IComparisonSettings options, int number)
    {
        const int FinalXorShift = 5;

        const string DocumentIdentityPrefix = "d";
        const string SharedCorpusText = "KeyLoad shared corpus";
        const string EmptyText = "";
        const char PayloadPaddingCharacter = 'x';
        const int AdjacentElementOffset = 1;
        const uint SeedMixMultiplierUint = 0x9e3779b9u;
        const int UninitializedRandomState = 0;
        const int NonzeroRandomSeed = 1;
        const int FirstElementIndex = 0;
        const int FirstXorShift = 13;
        const int ReverseXorShift = 17;
        const int VectorFractionMask = 0xffffff;
        const float VectorFractionDenominatorFloat = 8388608f;
        const float VectorCoordinateOffsetFloat = 1f;

        var id = DocumentIdentityPrefix + number.ToString(DocumentNumberFormat, System.Globalization.CultureInfo.InvariantCulture);
        var empty = JsonSerializer.Serialize(new
        {
            id,
            number,
            text = SharedCorpusText,
            padding = EmptyText
        });
        var json = JsonSerializer.Serialize(new
        {
            id,
            number,
            text = SharedCorpusText,
            padding = new string(PayloadPaddingCharacter, options.PayloadBytes - Encoding.UTF8.GetByteCount(empty))
        });
        var state = unchecked((uint)options.Seed ^ ((uint)number + AdjacentElementOffset) * SeedMixMultiplierUint);
        if (state == UninitializedRandomState)
        {
            state = NonzeroRandomSeed;
        }

        var vector = new float[options.Dimensions];
        for (var i = FirstElementIndex; i < vector.Length; i++)
        {
            state ^= state << FirstXorShift;
            state ^= state >> ReverseXorShift;
            state ^= state << FinalXorShift;
            vector[i] = (state & VectorFractionMask) / VectorFractionDenominatorFloat - VectorCoordinateOffsetFloat;
        }
        return new(number, id, json, ImmutableCollectionsMarshal.AsImmutableArray(vector));
    }
}
