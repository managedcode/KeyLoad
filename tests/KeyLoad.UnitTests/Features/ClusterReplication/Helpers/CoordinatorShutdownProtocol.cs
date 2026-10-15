namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class CoordinatorShutdownProtocol
{
    internal const string Root = "root";
    internal const string Collection = "coordinator-shutdown";
    internal const string Original = "original";
    internal const string Interrupted = "interrupted";
    internal const string Queued = "queued";
    internal const string Healthy = "healthy";
    internal const string Json = "{\"state\":\"healthy\"}";
    internal const string MissingFile = "original-missing-cancellation-image.bin";
    internal const string Put = "putDocument";
    internal const string ReplicaDirectory = "coordinator-native-replica";
    internal const int NoFailures = 0;
    internal const long FirstRevision = 1;
    internal const int ColdCycles = 2;
    internal const long NextPosition = 1;
    internal const long OwnershipEpoch = 1;
    internal static readonly global::KeyLoad.CommandAdmissionSnapshot EmptyAdmission = new(0, 0, 0, 0, 0, 0);
    internal const int ActiveCommands = 1;
    internal const int CallbackFailures = 1;
    internal const string InterruptedDetail = "Replication was interrupted. Retry the same command ID to resolve its outcome.";
    internal const string QueuedDetail = "The node stopped before returning a command outcome. Query or retry the same command ID.";
}
