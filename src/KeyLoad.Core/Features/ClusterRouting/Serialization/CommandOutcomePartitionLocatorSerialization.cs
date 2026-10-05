using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class CommandOutcomePartitionLocatorSerialization
{
    internal static byte[] LegacyKey(PartitionRef partition, string principalId, Guid commandId)
        => KeySpace.OutcomeLocatorV1(partition, principalId, commandId);

    internal static byte[] ScopedKey(PartitionRef partition, string principalId, Guid commandId)
        => KeySpace.OutcomeLocatorV2(partition, principalId, commandId);

    internal static byte[] LegacyValue(string principalId, Guid commandId)
        => KeySpace.LegacyOutcomeKey(principalId, commandId);

    internal static byte[] ScopedValue(PartitionRef partition, string principalId, Guid commandId)
        => KeySpace.PartitionOutcome(partition, principalId, commandId);

    internal static void WriteScoped(IAtomicTransaction transaction, PartitionRef partition, string principalId,
        Guid commandId)
    {
        transaction.Put(ScopedKey(partition, principalId, commandId), ScopedValue(partition, principalId, commandId));
    }

    internal static bool MatchesLegacy(IKeyValueView view, PartitionRef partition, string principalId, Guid commandId)
        => Matches(view, LegacyKey(partition, principalId, commandId), LegacyValue(principalId, commandId));

    internal static bool MatchesScoped(IKeyValueView view, PartitionRef partition, string principalId, Guid commandId)
        => Matches(view, ScopedKey(partition, principalId, commandId), ScopedValue(partition, principalId, commandId));

    private static bool Matches(IKeyValueView view, byte[] key, byte[] expected)
    {
        var stored = view.ReadOwnedValue(key);
        return stored is not null && stored.AsSpan().SequenceEqual(expected);
    }
}
