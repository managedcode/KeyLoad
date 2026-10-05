namespace KeyLoad.Comparisons;

internal static class VectorComparisonProfileValues
{
    internal const int FamilyPartIndex = 0;
    internal const int ScalePartIndex = 1;
    internal const int VectorDimensions = 128;
    internal const string Cosine = "Cosine";
    internal const int TopKNeighbors = 10;
    internal const int CorpusSeed = 1729;
    internal const int PayloadBytes = 1024;
    internal const int QueryVectorCount = 64;
    internal const int WarmupQueries = 256;
    internal const int Scale100kCount = 100_000;
    internal const int QueryConcurrency = 16;
    internal const int OperationTimeoutSeconds = 30;
    internal const int LatencySampleCount = 4096;
    internal const int RepetitionCount = 1;
    internal const double ExactRecall = 1d;
    internal const double ApproximateRecallTarget = 0.95d;
    internal const int MixedUpdateCount = 10_000;
    internal const int EmptyCount = 0;
    internal const char ProfileDelimiterCharacter = '-';
    internal const int ProfilePartCount = 5;
    internal const int ConcurrencyPartIndex = 4;
    internal const string UnknownVectorComparisonProfile = "Unknown vector comparison profile.";
    internal const int Scale1mCount = 1_000_000;
    internal const int MethodPartIndex = 2;
    internal const int ModePartIndex = 3;
    internal const string Vector = "vector-";
    internal const string ProfileDelimiter = "-";
    internal const string C16 = "-c16";
}
