using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ChangeFeedsValidation operations against the caller's database context.</summary>
public static class ChangeFeedsValidation
{
    /// <summary>Reads persisted outbox head metadata from the current storage cut.</summary>
    /// <param name = "view">Current gated storage view.</param>
    /// <param name = "partition">Owning atomic partition.</param>
    /// <returns>The persisted head, or its initial value.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static OutboxHead ReadOutboxHead(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(engine);
        const string InvalidHeadDetail = "The committed outbox head does not match its canonical retained range.";
        var head = view.GetRecord<OutboxHead>(KeySpace.Partition(ChangeFeedsExecutionProjectionOutbox.OutboxHeadSpace, partition)) ?? new(ChangeFeedsExecutionProjectionOutbox.ReadOutboxHeadTailEmptyCount, ChangeFeedsExecutionProjectionOutbox.ReadOutboxHeadFirstAvailableSingleItemCount, ChangeFeedsExecutionProjectionOutbox.ReadOutboxHeadStoredRecordsEmptyCount, ChangeFeedsExecutionProjectionOutbox.ReadOutboxHeadStoredBytesEmptyCount);
        if (head.Tail < ChangeFeedsExecutionProjectionOutbox.ReadOutboxHeadTailEmptyCount || head.FirstAvailable < ChangeFeedsExecutionProjectionOutbox.ReadOutboxHeadFirstAvailableSingleItemCount || head.FirstAvailable - ChangeFeedsExecutionProjectionOutbox.ReadOutboxHeadFirstAvailableSingleItemCount > head.Tail || head.StoredRecords != head.Tail - (head.FirstAvailable - ChangeFeedsExecutionProjectionOutbox.ReadOutboxHeadFirstAvailableSingleItemCount) || head.StoredBytes < ChangeFeedsExecutionProjectionOutbox.ReadOutboxHeadStoredBytesEmptyCount || (head.StoredRecords == ChangeFeedsExecutionProjectionOutbox.ReadOutboxHeadStoredRecordsEmptyCount) != (head.StoredBytes == ChangeFeedsExecutionProjectionOutbox.ReadOutboxHeadStoredBytesEmptyCount))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidHeadDetail);
        }

        return head;
    }

    internal static void RequireOutboxReclaimAccounting(OutboxHead head, long storedBytes)
    {
        const int SingleRetainedRecord = 1;
        const string InvalidStoredBytesDetail = "The committed outbox byte accounting does not match the reclaimed entry.";
        if (head.StoredRecords < SingleRetainedRecord || head.StoredBytes < storedBytes || (head.StoredRecords == SingleRetainedRecord) != (head.StoredBytes == storedBytes))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredBytesDetail);
        }
    }
}
