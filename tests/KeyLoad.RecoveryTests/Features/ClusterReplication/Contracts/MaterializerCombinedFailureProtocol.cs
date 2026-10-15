namespace KeyLoad.RecoveryTests;

internal static class MaterializerCombinedFailureProtocol
{
    internal const string DirectoryPrefix = "keyload-native-combined-shutdown-";
    internal const string FaultCanonicalDirectory = "canonical";
    internal const string ReplicaDirectory = "replica";
    internal const string MissingFile = "missing-native-work.bin";
    internal const string CallbackDetail = "The actual materializer cancellation callback failed.";
    internal const string MissingFileUnexpected = "The missing native filesystem control unexpectedly exists.";
    internal const string JoinNotProven = "The exact original materializer shutdown task has not joined.";
    internal const string OriginalPartitionKey = "original";
    internal const string LifetimeShapeChanged = "The exact native materializer lifetime shape changed.";
    internal const string EnqueueReceipt = "enqueue";
    internal const string LifetimeField = "lifetime";
    internal const string Principal = "combined-shutdown-owner";
    internal const string Tenant = "system";
    internal const string Wildcard = "*";
    internal const string Credential = "combined-shutdown-test-only-credential";
    internal const string WorkerDetail = "The transaction outcome is unknown. Recover and retry the same command ID.";
    internal const long Term = 1;
    internal const long FaultIndex = 1;
    internal const int OriginalFailures = 2;
    internal const int PlacementVersion = 1;
    internal const long NextPosition = 1;
    internal const long HealthyConfigurationIndex = 2;
    internal const long HealthyEnqueueIndex = 3;
    internal const long InitialStateVersion = 1;
    internal const long FirstReadySequence = 1;
    internal const string Queue = "healthy-recovered-queue";
    internal const string Message = "healthy";
    internal const string Payload = "{\"work\":92}";
    internal const string Headers = "{}";
    internal static PartitionRef Partition { get; } = new("combined-tenant", "combined-db", "combined-domain", OriginalPartitionKey);
    internal const int NoFailures = 0;
    internal const int NoAttempts = 0;
    internal const long NoQueueUsage = 0;
    internal const long SingleMessageCount = 1;
    internal static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
}
