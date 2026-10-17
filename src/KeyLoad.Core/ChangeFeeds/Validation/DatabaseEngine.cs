using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Reads persisted outbox head metadata from the current storage cut.</summary>
    /// <param name="view">Current gated storage view.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <returns>The persisted head, or its initial value.</returns>
    public OutboxHead ReadOutboxHead(IKeyValueView view, PartitionRef partition)
    {
        const string InvalidHeadDetail = "The committed outbox head does not match its canonical retained range.";
        var head = view.GetRecord<OutboxHead>(KeySpace.Partition(OutboxHeadSpace, partition))
            ?? new(ReadOutboxHeadTailEmptyCount, ReadOutboxHeadFirstAvailableSingleItemCount,
                ReadOutboxHeadStoredRecordsEmptyCount, ReadOutboxHeadStoredBytesEmptyCount);
        if (head.Tail < ReadOutboxHeadTailEmptyCount
            || head.FirstAvailable < ReadOutboxHeadFirstAvailableSingleItemCount
            || head.FirstAvailable - ReadOutboxHeadFirstAvailableSingleItemCount > head.Tail
            || head.StoredRecords != head.Tail - (head.FirstAvailable - ReadOutboxHeadFirstAvailableSingleItemCount)
            || head.StoredBytes < ReadOutboxHeadStoredBytesEmptyCount
            || (head.StoredRecords == ReadOutboxHeadStoredRecordsEmptyCount) != (head.StoredBytes == ReadOutboxHeadStoredBytesEmptyCount))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidHeadDetail);
        }
        return head;
    }

    private static void RequireOutboxReclaimAccounting(OutboxHead head, long storedBytes)
    {
        const int SingleRetainedRecord = 1;
        const string InvalidStoredBytesDetail = "The committed outbox byte accounting does not match the reclaimed entry.";
        if (head.StoredRecords < SingleRetainedRecord || head.StoredBytes < storedBytes
            || (head.StoredRecords == SingleRetainedRecord) != (head.StoredBytes == storedBytes))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredBytesDetail);
        }
    }
}
