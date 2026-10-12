using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class CommandOutcomePartitionLocatorSerialization
{
    internal static byte[] ScopedKey(PartitionRef partition, string principalId, Guid commandId)
        => KeySpace.OutcomeLocatorV2(partition, principalId, commandId);

    internal static byte[] ScopedValue(PartitionRef partition, string principalId, Guid commandId)
        => KeySpace.PartitionOutcome(partition, principalId, commandId);

    internal static void WriteScoped(IAtomicTransaction transaction, PartitionRef partition, string principalId,
        Guid commandId)
    {
        transaction.Put(ScopedKey(partition, principalId, commandId), ScopedValue(partition, principalId, commandId));
    }

    internal static bool MatchesScoped(IKeyValueView view, PartitionRef partition, string principalId, Guid commandId,
        StorageReadObserver? observer = null)
        => Matches(view, ScopedKey(partition, principalId, commandId), ScopedValue(partition, principalId, commandId), observer);

    private static bool Matches(IKeyValueView view, byte[] key, byte[] expected, StorageReadObserver? observer)
    {
        var stored = ReadScopedValue(view, key, observer);
        return stored is not null && stored.AsSpan().SequenceEqual(expected);
    }
    internal static byte[]? ReadScopedValue(IKeyValueView view, byte[] key, StorageReadObserver? observer)
    {
        if (observer is null)
        { return view.ReadOwnedValue(key); }
        byte[]? value = null;
        view.ReadValue(key, borrowed => value = borrowed.ToArray(), observer);
        return value;
    }
}
