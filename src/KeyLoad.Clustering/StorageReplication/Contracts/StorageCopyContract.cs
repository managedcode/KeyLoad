namespace KeyLoad.Clustering;

internal static class StorageCopyContract
{
    internal const int MinimumCount = 1;
    internal const int UnpersistedCopies = 0;
    internal const int FirstOwner = 0;
    internal const int LastOwnerFromEnd = 1;
    internal const long NextSequence = 1;
    internal const string InvalidIncarnation = "A database incarnation is required.";
    internal const string InvalidCounts = "StorageMode must be supported; silo and copy counts must be positive, copies cannot exceed silos, and SharedVolume requires ReplicaCount 1.";
    internal const string DifferentOutcomes = "The persisted data-copy outcomes differ.";
    internal const string WriteUncertain = "The write did not receive every required persistence acknowledgement.";
    internal const string OwnersUnavailable = "The required Orleans storage services are unavailable.";
    internal const string RecoveryRequired = "The storage copies require recovery before another operation.";
    internal const string SequenceExhausted = "The canonical storage sequence is exhausted.";
}
