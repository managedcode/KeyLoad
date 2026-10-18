namespace KeyLoad.Comparisons;

internal static class BenchmarkSessionLimits
{
    internal const int NoObservedItems = 0;
    internal const int SingleItemCount = 1;
    internal const int StableMutationGroups = 2;
    internal const int DocumentMixedLanes = 4;
    internal const int QueryMixedLanes = 5;
    internal const int MaximumClients = 128;
    internal const int MaximumTimeoutSeconds = 120;
    internal const int InitialRecords = 1_000_000;
    internal const int StreamOperations = 100_000;
    internal const int Clients = 16;
    internal const int TimeoutSeconds = 30;
    internal const int MinimumRecordGroups = 3;
    internal const int ReadStride = 7919;
    internal const int CorpusSeed = 1729;
    internal const int ProgressCadence = 1024;
    internal const int MillisecondsPerSecond = 1000;
    internal const int BeforeFirstOperation = -1;
    internal const int SingleNodeCount = 1;
    internal const int ReplicatedNodeCount = 3;
}
