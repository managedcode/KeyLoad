namespace KeyLoad.Replication;

internal static class StorageCopyContract
{
    internal const int MinimumCount = 1;
    internal const int FirstOwner = 0;
    internal const string ServiceAlias = "keyload.silo-database-service";
    internal const string GrainAlias = "keyload.database-commit-grain";
    internal const string CutAlias = "keyload.silo-storage-cut";
    internal const string ReadCut = "read-cut";
    internal const string Cut = "cut";
    internal const string Prepare = "prepare";
    internal const string Apply = "apply";
    internal const string InvalidIncarnation = "A database incarnation is required.";
    internal const string InvalidCounts = "SiloCount and ReplicaCount must be positive; ReplicaCount cannot exceed SiloCount.";
    internal const string DifferentOutcomes = "The persisted data-copy outcomes differ.";
    internal const string WriteUncertain = "The write did not receive every required persistence acknowledgement.";
    internal const string OwnersUnavailable = "The required Orleans storage services are unavailable.";
    internal const string RecoveryRequired = "The storage copies require recovery before another operation.";
}
