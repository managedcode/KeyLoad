using KeyLoad.Core;

namespace KeyLoad.UnitTests;

internal static class ExpectedCanonicalCommit
{
    internal static CommitToken Next(DatabaseEngine database, PartitionRef partition) => database.Store.Read<CommitToken>(view =>
    {
        var placement = database.ReadAtomicPartitionPlacement(view, "root", partition);
        var applied = view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? NativeSerialization.Deserialize<long>(bytes) : 0L;
        return new(placement.Incarnation, partition.AtomicPartitionId, checked(applied + 1L));
    });
}
