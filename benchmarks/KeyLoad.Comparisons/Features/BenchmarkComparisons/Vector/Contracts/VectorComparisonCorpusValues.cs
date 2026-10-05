namespace KeyLoad.Comparisons;

internal static class VectorComparisonCorpusValues
{
    internal const string VectorIdPrefix = "v";
    internal const string CanonicalPayloadPrefix = "{\"id\":\"";
    internal const string CanonicalNumberPrefix = "\",\"number\":";
    internal const string CanonicalPaddingPrefix = ",\"padding\":\"";
    internal const char PayloadPaddingCharacter = 'x';
    internal const int PayloadSuffixCharacterCount = 2;
    internal const string CanonicalPayloadSuffix = "\"}";
    internal const int FirstIndex = 0;
    internal const int SeedShift = 32;
    internal const ulong UpdatedEmbeddingMask = 0xd1b54a32d192ed03UL;
    internal const ulong UnchangedEmbeddingMask = 0UL;
    internal const ulong SplitMixIncrement = 0x9e3779b97f4a7c15UL;
    internal const int MutableGroupWidth = 10;
    internal const ulong MixedUpdateStride = 2654435761UL;
    internal const int MutableRecordSuffix = 9;
    internal const int FilterModulo = 100;
    internal const int FloatByteCount = 4;
    internal const string EmbeddingDimensionsDoNotMatchProfile = "Embedding dimensions do not match profile.";
    internal const double ZeroSquaredNorm = 0d;
    internal const int FirstMixShift = 30;
    internal const ulong SplitMixFirstMultiplier = 0xbf58476d1ce4e5b9UL;
    internal const int SecondMixShift = 27;
    internal const ulong SplitMixSecondMultiplier = 0x94d049bb133111ebUL;
    internal const int FinalMixShift = 31;
    internal const int FractionShift = 40;
    internal const int FractionLeadingBit = 1;
    internal const int FractionBits = 24;
    internal const float UnitVectorScale = 2f;
    internal const float UnitVectorOffset = 1f;
}
