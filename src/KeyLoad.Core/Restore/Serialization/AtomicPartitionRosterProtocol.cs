namespace KeyLoad.Core.BackupRestore.Serialization;

internal static class AtomicPartitionRosterProtocol
{
    internal const int CurrentVersion = 1;
    internal const long NoCanonicalSequence = 0;
    internal const long NoStorePosition = 0;
    internal const string KeySpace = "atomic-partition-catalog";
    internal const string PartitionKeyKind = "partition";
    internal const string MalformedScopedKey = "A committed partition-scoped key is malformed.";
    internal const string InvalidEntry = "A committed atomic partition roster entry is malformed.";
}
