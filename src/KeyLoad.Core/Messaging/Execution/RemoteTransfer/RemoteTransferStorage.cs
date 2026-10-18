using ManagedCode.Communication;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferStorage
{
    private const int InitialSequence = 0;
    private const int NoRetainedBytes = 0;
    private const int NoRetainedRecords = 0;
    private const int CapacityWitnessRecords = 1;
    private const int AdjacentElementOffset = 1;
    private const string RetainedQueueTransferCapacityIsExhaustedDetail = "The retained queue transfer capacity is exhausted.";

    private const string CorruptCapacityMessage = "Persisted queue transfer capacity is inconsistent.";

    internal static bool IsCapacityFailure(Result<object> result)
        => Errors.Code(result.Problem) == ErrorCode.ResourceExhausted && result.Problem?.Detail == RetainedQueueTransferCapacityIsExhaustedDetail;

    internal static byte[] IntentKey(QueueLaneRef source, Guid transferId)
        => KeySpace.Partition(RemoteTransferProtocol.IntentSpace, source.Partition, source.Queue,
            transferId.ToString(RemoteTransferProtocol.TransferIdFormat));

    internal static byte[] SourceCapacityKey(QueueLaneRef source)
        => KeySpace.Partition(RemoteTransferProtocol.SourceCapacitySpace, source.Partition, source.Queue);

    internal static byte[] TargetReceiptKey(QueueLaneRef source, Guid transferId, QueueLaneRef destination)
        => KeySpace.Partition(RemoteTransferProtocol.TargetReceiptSpace, destination.Partition, destination.Queue,
            source.Partition.TenantId, source.Partition.DatabaseId, source.Partition.TransactionDomainId,
            source.Partition.PartitionKey, source.Queue, transferId.ToString(RemoteTransferProtocol.TransferIdFormat));

    internal static byte[] TargetCapacityKey(QueueLaneRef destination)
        => KeySpace.Partition(RemoteTransferProtocol.TargetCapacitySpace, destination.Partition, destination.Queue);

    internal static RemoteTransferCapacity SourceCapacity(IKeyValueView view, QueueLaneRef source)
        => ReadCapacity(view, SourceCapacityKey(source),
            KeySpace.Partition(RemoteTransferProtocol.IntentSpace, source.Partition, source.Queue), required: false);

    internal static RemoteTransferCapacity TargetCapacity(IKeyValueView view, QueueLaneRef destination)
        => ReadCapacity(view, TargetCapacityKey(destination),
            KeySpace.Partition(RemoteTransferProtocol.TargetReceiptSpace, destination.Partition, destination.Queue), required: false);

    internal static RemoteTransferCapacity RequireSourceCounter(IKeyValueView view, QueueLaneRef source)
        => ReadCapacity(view, SourceCapacityKey(source),
            KeySpace.Partition(RemoteTransferProtocol.IntentSpace, source.Partition, source.Queue), required: true);

    internal static RemoteTransferCapacity RequireTargetCounter(IKeyValueView view, QueueLaneRef destination)
        => ReadCapacity(view, TargetCapacityKey(destination),
            KeySpace.Partition(RemoteTransferProtocol.TargetReceiptSpace, destination.Partition, destination.Queue), required: true);

    internal static long SourceAccountedBytes(RemoteTransferIntentRecord record)
    {
        var actualReceiptBytes = record.ReceiptToken is null ? NoRetainedBytes : Encoding.UTF8.GetByteCount(record.ReceiptToken);
        if (actualReceiptBytes > record.ReceiptReservationBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptCapacityMessage);
        }
        return checked(SerializedBytes(record) + record.ReceiptReservationBytes - actualReceiptBytes);
    }

    internal static long SerializedBytes<T>(T record) => NativeSerialization.Serialize(record).LongLength;

    internal static RemoteTransferCapacity Add(RemoteTransferCapacity current, long bytes)
        => new(checked(current.StoredRecords + AdjacentElementOffset), checked(current.StoredBytes + bytes));

    internal static RemoteTransferCapacity Replace(RemoteTransferCapacity current, long oldBytes, long newBytes,
        DatabaseLimits limits)
    {
        RequireCounter(current);
        if (oldBytes < NoRetainedBytes || newBytes < NoRetainedBytes || oldBytes > current.StoredBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptCapacityMessage);
        }
        var retainedBytes = current.StoredBytes - oldBytes;
        if (newBytes > limits.MaxBatchBytes - retainedBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RetainedQueueTransferCapacityIsExhaustedDetail);
        }
        return new(current.StoredRecords, checked(retainedBytes + newBytes));
    }

    internal static void RequireCapacity(RemoteTransferCapacity current, long additionalBytes, DatabaseLimits limits)
    {
        RequireCounter(current);
        if (current.StoredRecords >= limits.MaxScanRecords
            || additionalBytes < NoRetainedBytes || additionalBytes > limits.MaxBatchBytes - current.StoredBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RetainedQueueTransferCapacityIsExhaustedDetail);
        }
    }

    private static RemoteTransferCapacity ReadCapacity(IKeyValueView view, byte[] key, byte[] prefix, bool required)
    {
        var retained = view.GetRecord<RemoteTransferCapacity>(key);
        var current = RequireCounter(retained ?? (required ? null : new(NoRetainedRecords, NoRetainedBytes)));
        long firstBytes = NoRetainedBytes;
        var witness = view.VisitRange(prefix, CapacityWitnessRecords, (_, value) =>
        {
            firstBytes = value.Length;
            return true;
        });
        if ((witness.Records == NoRetainedRecords) != (current.StoredRecords == NoRetainedRecords)
            || current.StoredBytes < firstBytes
            || witness.HasMore && current.StoredRecords <= CapacityWitnessRecords)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptCapacityMessage);
        }
        return current;
    }

    private static RemoteTransferCapacity RequireCounter(RemoteTransferCapacity? capacity)
    {
        if (capacity is null || capacity.StoredRecords < InitialSequence || capacity.StoredBytes < NoRetainedBytes
            || (capacity.StoredRecords == NoRetainedRecords) != (capacity.StoredBytes == NoRetainedBytes))
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptCapacityMessage);
        }
        return capacity;
    }
}
