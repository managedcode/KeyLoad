using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Messaging;

internal static class RecurringSagaStorage
{
    private const string CorruptCapacity = "Persisted recurring schedule and saga capacity is inconsistent.";

    internal static byte[] ScheduleKey(QueueLaneRef lane, Guid scheduleId)
        => KeySpace.Partition(RecurringSagaProtocol.ScheduleSpace, lane.Partition, lane.Queue,
            scheduleId.ToString(RecurringSagaProtocol.IdentifierFormat));

    internal static byte[] SagaKey(QueueLaneRef lane, Guid sagaId)
        => KeySpace.Partition(RecurringSagaProtocol.SagaSpace, lane.Partition, lane.Queue,
            sagaId.ToString(RecurringSagaProtocol.IdentifierFormat));

    internal static byte[] CapacityKey(QueueLaneRef lane)
        => KeySpace.Partition(RecurringSagaProtocol.CapacitySpace, lane.Partition, lane.Queue);

    internal static RecurringSagaCapacity Capacity(IKeyValueView view, QueueLaneRef lane)
        => view.GetRecord<RecurringSagaCapacity>(CapacityKey(lane)) ?? new(0, 0);

    internal static RecurringSagaCapacity RequireCapacity(IKeyValueView view, QueueLaneRef lane)
        => Require(view.GetRecord<RecurringSagaCapacity>(CapacityKey(lane)));

    internal static long SerializedBytes<T>(T value) => NativeSerialization.Serialize(value).LongLength;

    internal static RecurringSagaCapacity Add(RecurringSagaCapacity current, long bytes, DatabaseLimits limits)
    {
        RequireCounter(current);
        var records = checked(current.Records + 1);
        var totalBytes = checked(current.Bytes + bytes);
        if (records > limits.MaxScanRecords || bytes < 0 || totalBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RecurringSagaProtocol.CapacityExhausted);
        }
        return new(records, totalBytes);
    }

    internal static RecurringSagaCapacity Replace(RecurringSagaCapacity current, long oldBytes, long newBytes,
        DatabaseLimits limits)
    {
        RequireCounter(current);
        if (oldBytes < 0 || newBytes < 0 || oldBytes > current.Bytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptCapacity);
        }
        var totalBytes = checked(current.Bytes - oldBytes + newBytes);
        if (totalBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RecurringSagaProtocol.CapacityExhausted);
        }
        return current with { Bytes = totalBytes };
    }

    private static RecurringSagaCapacity Require(RecurringSagaCapacity? capacity)
    {
        if (capacity is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptCapacity);
        }
        RequireCounter(capacity);
        return capacity;
    }

    private static void RequireCounter(RecurringSagaCapacity capacity)
    {
        if (capacity is null || capacity.Records < 0 || capacity.Bytes < 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptCapacity);
        }
    }
}
