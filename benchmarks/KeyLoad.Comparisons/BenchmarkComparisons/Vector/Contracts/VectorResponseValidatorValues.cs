namespace KeyLoad.Comparisons;

internal static class VectorResponseValidatorValues
{
    internal const int FirstIndex = 0;
    internal const string AVectorResponseWasUnderfilledOr = "A vector response was underfilled or overfilled.";
    internal const double ExactRecall = 1d;
    internal const string AnExactVectorResponseDiffersFrom = "An exact vector response differs from the independent oracle.";
    internal const string ANativeVectorResponseIsNot = "A native vector response is not ordered by distance and ordinal identity.";
    internal const string ANativeVectorResponseContainsDuplicate = "A native vector response contains duplicate IDs.";
    internal const double CosineTolerance = 0.000001d;
    internal const double MaximumCosineWithTolerance = 2.000001d;
    internal const string ANativeVectorResponseContainsMissing = "A native vector response contains missing IDs or invalid cosine distances.";
    internal const int IdCharacterCount = 10;
    internal const char VectorIdPrefixCharacter = 'v';
    internal const int SingleElementOffset = 1;
    internal const string ANativeVectorResponseIncludedAn = "A native vector response included an unknown or ineligible document.";
}
