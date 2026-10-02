using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ChangeFeeds;

internal sealed record StoredOutboxEntry(OutboxEntry Entry, byte[] Key, int StoredBytes);

internal static class StoredOutboxReader
{
    private const string OutboxSpace = "outbox";
    private const string CorruptEntryMessage = "The committed outbox contains a sequence gap.";

    public static IEnumerable<StoredOutboxEntry> ReadRange(IKeyValueView view, PartitionRef partition,
        long after, long tail, int limit)
    {
        var partitionId = partition.AtomicPartitionId;
        for (var offset = 1; offset <= limit && offset <= tail - after; offset++)
        {
            var sequence = checked(after + offset);
            var key = KeySpace.Partition(OutboxSpace, partition, sequence);
            OutboxEntry? entry = null;
            var storedBytes = 0;
            if (!view.ReadValue(key, value =>
            {
                storedBytes = value.Length;
                entry = JsonDefaults.Deserialize<OutboxEntry>(value);
            }) || entry is null || entry.Sequence != sequence
                || entry.Commit.AtomicPartitionId != partitionId)
            {
                throw Errors.Fail(ErrorCode.Corruption, CorruptEntryMessage);
            }

            yield return new(entry, key, storedBytes);
        }
    }
}
