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
        var head = view.GetRecord<OutboxHead>(KeySpace.Partition(ProjectionOutbox.OutboxHeadSpace, partition)) ?? new(ProjectionOutbox.ReadOutboxHeadTailEmptyCount, ProjectionOutbox.ReadOutboxHeadFirstAvailableSingleItemCount, ProjectionOutbox.ReadOutboxHeadStoredRecordsEmptyCount, ProjectionOutbox.ReadOutboxHeadStoredBytesEmptyCount);
        if (head.Tail < ProjectionOutbox.ReadOutboxHeadTailEmptyCount || head.FirstAvailable < ProjectionOutbox.ReadOutboxHeadFirstAvailableSingleItemCount || head.FirstAvailable - ProjectionOutbox.ReadOutboxHeadFirstAvailableSingleItemCount > head.Tail || head.StoredRecords != head.Tail - (head.FirstAvailable - ProjectionOutbox.ReadOutboxHeadFirstAvailableSingleItemCount) || head.StoredBytes < ProjectionOutbox.ReadOutboxHeadStoredBytesEmptyCount || (head.StoredRecords == ProjectionOutbox.ReadOutboxHeadStoredRecordsEmptyCount) != (head.StoredBytes == ProjectionOutbox.ReadOutboxHeadStoredBytesEmptyCount))
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
