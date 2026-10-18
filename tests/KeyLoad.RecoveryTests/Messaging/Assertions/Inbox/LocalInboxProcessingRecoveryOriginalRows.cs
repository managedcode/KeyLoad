using ManagedCode.Communication.Commands;
using KeyLoad.Core;
using KeyLoad.RecoveryHost.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryOriginalRows
{
    private const string OutboxFamily = "outbox";
    private const string MissingOriginal = "An original processing outbox, inbox or outcome row is missing.";
    private const int OutcomeRowOrdinal = 1;
    private const int LocatorRowOrdinal = 2;
    private const int FirstMutationOrdinal = 0;
    private const int NextOutboxSequence = 1;

    internal static (byte[] Key, byte[] Value)[] Capture(ZoneTreeStore store, Command<DatabaseCommand> original, long seedTail)
    {
        var partition = LocalInboxProcessingCrashContract.Partition;
        var keys = new List<byte[]>
        {
            LocalInboxProcessingRecoveryImage.InboxKey(),
            KeySpace.PartitionOutcome(partition, original.UserId!, original.CommandId),
            KeySpace.OutcomeLocatorV2(partition, original.UserId!, original.CommandId)
        };
        for (var ordinal = FirstMutationOrdinal; ordinal < LocalInboxProcessingCrashContract.MutationCount; ordinal++)
        {
            keys.Add(KeySpace.Partition(OutboxFamily, partition, checked(seedTail + ordinal + NextOutboxSequence)));
        }
        return store.Read(view => keys.Select(key => (key,
            view.ReadOwnedValue(key) ?? throw new InvalidOperationException(MissingOriginal))).ToArray());
    }

    internal static async Task SameAsync(ZoneTreeStore store, (byte[] Key, byte[] Value)[] original)
    {
        await Assert.That(original[LocatorRowOrdinal].Value.AsSpan()
            .SequenceEqual(original[OutcomeRowOrdinal].Key)).IsTrue();
        foreach (var row in original)
        {
            var actual = store.Read(view => view.ReadOwnedValue(row.Key));
            await Assert.That(actual).IsNotNull();
            await Assert.That(actual!.AsSpan().SequenceEqual(row.Value)).IsTrue();
        }
    }
}
