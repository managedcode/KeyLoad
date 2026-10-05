using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class CommandOutcomePartitionLocatorSerialization
{
    internal static byte[] Key(PartitionRef partition, string principalId, Guid commandId)
        => KeySpace.OutcomeLocator(partition, principalId, commandId);

    internal static byte[] Value(string principalId, Guid commandId)
        => KeySpace.Outcome(principalId, commandId);

    internal static void Write(IAtomicTransaction transaction, PartitionRef partition, string principalId,
        Guid commandId)
    {
        transaction.Put(Key(partition, principalId, commandId), Value(principalId, commandId));
    }

    internal static bool Matches(IKeyValueView view, PartitionRef partition, string principalId, Guid commandId)
    {
        var stored = view.ReadOwnedValue(Key(partition, principalId, commandId));
        return stored is not null && stored.AsSpan().SequenceEqual(Value(principalId, commandId));
    }
}
