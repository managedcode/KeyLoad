namespace KeyLoad.Comparisons;

internal static class DocumentMeasurementValues
{
    internal const int HistogramResolutionMicroseconds = 10;
    internal const int DocumentIdentifierLength = 10;
    internal const int ReadUpdate50CycleLength = 2;
    internal const int MixedCrudUpdatePosition = 2;
    internal const int PermutationStrideIncrement = 2;
    internal const int WarmupCorpusMultiplier = 2;
    internal const int DocumentSchemaVersion = 1;
    internal const int DefaultUnusedTopK = 10;
    internal const int BaselineConcurrentClients = 10;
    internal const int CanonicalPayloadBytes = 1024;
    internal const int CanonicalRepetitions = 3;
    internal const int CanonicalSeed = 1729;
    internal const int CanonicalWarmup = 256;
    internal const int DevelopmentMaximumOperations = 1000;
    internal const int DevelopmentMaximumRecords = 1024;
    internal const int DevelopmentMinimumRecords = 100;
    internal const int DocumentSchemaDimensions = 32;
    internal const int IngestionRecords = 1000000;
    internal const int MaximumClients = 500;
    internal const int MaximumFailureCategories = 64;
    internal const int MaximumLatencyMilliseconds = 30_000;
    internal const double MedianQuantile = .5;
    internal const double MicrosecondsPerMillisecondFloatingPoint = 1000;
    internal const int MicrosecondsPerMillisecond = 1000;
    internal const int MixedCrudCycleLength = 4;
    internal const int NamespacesPerRepetition = 2;
    internal const int NativeActualEnumeration = -1;
    internal const int NoObservedItems = 0;
    internal const int OperationTimeoutSeconds = 30;
    internal const int OrdinaryClients = 16;
    internal const int ReadPermutationStride = 7919;
    internal const int ReadUpdate95CeilingAdjustment = 19;
    internal const int ReadUpdate95CycleLength = 20;
    internal const int ReadUpdate95Percentage = 5;
    internal const int SingleItemCount = 1;
    internal const int SmallDatasetRecords = 100000;
    internal const double Tail95Quantile = .95;
    internal const double Tail99Quantile = .99;
}
