using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ChangeFeeds;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.StoredOutboxEntry)]
internal sealed record StoredOutboxEntry(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutboxEntryFields.Entry)] OutboxEntry Entry,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutboxEntryFields.Key)] byte[] Key,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutboxEntryFields.StoredBytes)] int StoredBytes);

internal static class StoredOutboxReader
{
    private const string OutboxSpace = "outbox";
    private const string CorruptEntryMessage = "The committed outbox contains a sequence gap.";

    public static IEnumerable<StoredOutboxEntry> ReadRange(IKeyValueView view, PartitionRef partition,
        long after, long tail, int limit)
    {
        const int OffsetInitialValue = 1;
        const int StoredBytesInitialValue = 0;

        var partitionId = partition.AtomicPartitionId;
        for (var offset = OffsetInitialValue; offset <= limit && offset <= tail - after; offset++)
        {
            var sequence = checked(after + offset);
            var key = KeySpace.Partition(OutboxSpace, partition, sequence);
            OutboxEntry? entry = null;
            var storedBytes = StoredBytesInitialValue;
            if (!view.ReadValue(key, value =>
            {
                storedBytes = value.Length;
                entry = NativeSerialization.Deserialize<OutboxEntry>(value);
            }) || entry is null || entry.Sequence != sequence
                || entry.Commit.AtomicPartitionId != partitionId)
            {
                throw Errors.Fail(ErrorCode.Corruption, CorruptEntryMessage);
            }

            yield return new(entry, key, storedBytes);
        }
    }
}
