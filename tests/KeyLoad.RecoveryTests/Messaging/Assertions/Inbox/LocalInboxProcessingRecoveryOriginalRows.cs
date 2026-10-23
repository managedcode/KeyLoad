using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryOriginalRows
{
    private const string OutboxFamily = "outbox";
    private const string MissingOriginal = "An original processing outbox, inbox or outcome row is missing.";
    private const int LocatorRowOrdinal = 2;

    internal static (byte[] Key, byte[] Value)[] Capture(ZoneTreeStore store, Command<DatabaseCommand> original, long seedTail)
    {
        var partition = LocalInboxProcessingCrashContract.Partition;
        var keys = new List<byte[]>
        {
            LocalInboxProcessingRecoveryImage.InboxKey(),
            KeySpace.PartitionOutcome(partition, original.UserId!, original.CommandId),
            KeySpace.OutcomeLocator(partition, original.UserId!, original.CommandId)
        };
        for (var ordinal = 0; ordinal < LocalInboxProcessingCrashContract.MutationCount; ordinal++)
        {
            keys.Add(KeySpace.Partition(OutboxFamily, partition, checked(seedTail + ordinal + 1)));
        }
        return store.Read(view => keys.Select(key => (key,
            view.ReadOwnedValue(key) ?? throw new InvalidOperationException(MissingOriginal))).ToArray());
    }

    internal static async Task SameAsync(ZoneTreeStore store, (byte[] Key, byte[] Value)[] original)
    {
        await Assert.That(original[LocatorRowOrdinal].Value.AsSpan()
            .SequenceEqual(original[1].Key)).IsTrue();
        foreach (var row in original)
        {
            var actual = store.Read(view => view.ReadOwnedValue(row.Key));
            await Assert.That(actual).IsNotNull();
            await Assert.That(actual!.AsSpan().SequenceEqual(row.Value)).IsTrue();
        }
    }
}
