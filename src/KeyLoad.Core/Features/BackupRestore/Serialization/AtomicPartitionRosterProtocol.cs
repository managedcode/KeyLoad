namespace KeyLoad.Core.Features.BackupRestore.Serialization;

internal static class AtomicPartitionRosterProtocol
{
    internal const int CurrentVersion = 1;
    internal const long NoReplicatedAppliedIndex = 0;
    internal const long NoReplicatedStorePosition = 0;
    internal const string KeySpace = "atomic-partition-catalog";
    internal const string KeyVersion = "v1";
    internal const string PartitionKeyKind = "partition";
    internal const string MalformedScopedKey = "A committed partition-scoped key is malformed.";
    internal const string MalformedPlacement = "A committed atomic partition placement is malformed.";
    internal const string InvalidEntry = "A committed atomic partition roster entry is malformed.";
    internal const string CandidateBudgetExhausted = "The operation exceeds its partition roster candidate budget.";
}
