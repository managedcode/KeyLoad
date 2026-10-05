namespace KeyLoad.Comparisons;

internal static class VectorExactOracleValues
{
    internal const int FirstIndex = 0;
    internal const int CancellationChunkMask = 4095;
    internal const string VectorIdPrefix = "v";
    internal const double ExactRecall = 1d;
    internal const string TheQueryDimensionDiffersFromThe = "The query dimension differs from the profile.";
    internal const string TheQueryMustHaveAFinite = "The query must have a finite nonzero norm.";
    internal const double ZeroObservation = 0d;
}
