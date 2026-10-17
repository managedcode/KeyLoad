namespace KeyLoad.Comparisons;

internal static class ScaledComparisonProfileValues
{
    internal const int Scale100kCount = 100_000;
    internal const int WarmupQueries = 256;
    internal const int RepetitionCount = 1;
    internal const int QueryConcurrency = 16;
    internal const int PayloadBytes = 1_024;
    internal const int CorpusSeed = 1_729;
    internal const int VectorDimensions = 32;
    internal const int TopKNeighbors = 10;
    internal const int OperationTimeoutSeconds = 30;
    internal const int NoGraphEntities = 0;
}
