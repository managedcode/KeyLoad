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
    private const string CorruptDocumentMutation = "The committed document mutation does not match its outbox receipt.";
    private const string PutKind = "putDocument";
    private const string PatchKind = "patchDocument";
    private const string DeleteKind = "deleteDocument";

    private static void RequireDocumentMutationReceipt(OutboxEntry entry)
    {
        (string Resource, string Id, string Kind)? expected = entry.Mutation switch
        {
            PutDocument put => (put.Collection, put.Id, PutKind),
            PatchDocument patch => (patch.Collection, patch.Id, PatchKind),
            DeleteDocument delete => (delete.Collection, delete.Id, DeleteKind),
            _ => null
        };
        if (expected is { } document && (entry.Mutation.Resource != document.Resource
            || entry.Receipt.Resource != document.Resource || entry.Receipt.Id != document.Id
            || entry.Receipt.Kind != document.Kind || entry.After is not { } after
            || entry.Receipt.Revision != after.Revision))
        { throw Errors.Fail(ErrorCode.Corruption, CorruptDocumentMutation); }
    }

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

            RequireDocumentMutationReceipt(entry);
            yield return new(entry, key, storedBytes);
        }
    }
}
