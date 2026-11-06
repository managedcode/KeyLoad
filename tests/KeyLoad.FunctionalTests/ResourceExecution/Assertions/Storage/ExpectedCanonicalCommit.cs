namespace KeyLoad.FunctionalTests;

internal static class ExpectedCanonicalCommit
{
    internal static long Applied(DatabaseEngine database, PartitionRef? partition)
        => NativeStoreAssertions.Applied(database.Store, partition);

    internal static CommitToken Next(DatabaseEngine database, PartitionRef partition) => database.Store.Read<CommitToken>(view =>
    {
        var placement = database.ReadAtomicPartitionPlacement(view, "root", partition);
        var applied = NativeStoreAssertions.AppliedInView(view, partition);
        return new(placement.Incarnation, partition.AtomicPartitionId, checked(applied + 1L));
    });
}
