using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class DueWorkRecordDecoder
{
    internal static DueWorkHint? Read(DatabaseEngine database, DueWorkKind kind, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value, DateTimeOffset wakeAt)
        => kind switch
        {
            DueWorkKind.Schedule => ReadSchedule(database, key, value, wakeAt),
            DueWorkKind.Saga => ReadSaga(database, key, value, wakeAt),
            _ => throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord)
        };

    internal static byte[] CopyKey(ReadOnlySpan<byte> key)
    {
        if (key.Length > DueWorkProtocol.MaximumKeyBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.KeyExceedsBound);
        }
        if (key.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidCursor);
        }
        return key.ToArray();
    }

    internal static ErrorCode SafeError(ErrorCode code)
        => code is ErrorCode.Validation or ErrorCode.ResourceExhausted ? ErrorCode.Corruption : code;

    private static DueWorkHint? ReadSchedule(DatabaseEngine database, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value, DateTimeOffset wakeAt)
    {
        var record = NativeSerialization.Deserialize<RecurringScheduleRecord>(value);
        ValidateSchedule(database, key, record);
        var dueAt = OccurrenceTime(record.Definition, record.NextOrdinal);
        ValidateOccurrenceExpiry(record.Definition, dueAt);
        if (record.Cancelled)
        {
            return null;
        }
        return dueAt <= wakeAt
            ? new(DueWorkKind.Schedule, record.Lane, record.ScheduleId, record.CreatorPrincipalId,
                record.Revision, record.Generation, record.NextOrdinal, dueAt)
            : null;
    }

    private static DueWorkHint? ReadSaga(DatabaseEngine database, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value, DateTimeOffset wakeAt)
    {
        var record = NativeSerialization.Deserialize<SagaRecord>(value);
        ValidateSaga(database, key, record);
        return record.Phase == SagaPhase.Waiting && record.Deadline is { } dueAt && dueAt <= wakeAt
            ? new(DueWorkKind.Saga, record.Lane, record.SagaId, record.CreatorPrincipalId,
                record.Revision, 0, 0, dueAt)
            : null;
    }

    private static void ValidateSchedule(DatabaseEngine database, ReadOnlySpan<byte> key,
        RecurringScheduleRecord record)
    {
        if (HasInvalidScheduleShape(record))
        {
            throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord);
        }
        ValidateLane(record.Lane!);
        JsonData.Identifier(record.CreatorPrincipalId!);
        ValidateKey(key, DueWorkProtocol.ScheduleSpace, record.Lane!, record.ScheduleId);
        _ = JsonData.Validate(record.Definition!.PayloadJson!, database.Limits);
        _ = JsonData.Validate(record.Definition!.HeadersJson!, database.Limits);
        if (record.Definition.OrderingKey is { } orderingKey)
        {
            JsonData.Identifier(orderingKey);
        }
    }

    private static bool HasInvalidScheduleShape(RecurringScheduleRecord record)
        => HasInvalidScheduleIdentity(record) || HasInvalidScheduleDefinition(record);

    private static bool HasInvalidScheduleIdentity(RecurringScheduleRecord record)
        => record is null || record.Lane is null || record.Lane.Partition is null || record.Definition is null
            || record.CreatorPrincipalId is null || record.ScheduleId == Guid.Empty || record.Revision < 1
            || record.Generation < 1 || record.NextOrdinal < 0 || record.Lane != record.Definition.Lane
            || record.ScheduleId != record.Definition.ScheduleId;

    private static bool HasInvalidScheduleDefinition(RecurringScheduleRecord record)
        => record.Definition!.FirstDueAt.Offset != TimeSpan.Zero
            || record.Definition.TimeZone != DueWorkFields.UtcZone
            || record.Definition.PayloadJson is null || record.Definition.HeadersJson is null
            || record.Definition.Interval.Ticks is < DueWorkFields.MinimumIntervalTicks or > DueWorkFields.MaximumIntervalTicks
            || record.Definition.Misfire != RecurringMisfirePolicy.CatchUp
            || record.Definition.MessageTimeToLive is { } ttl && (ttl <= TimeSpan.Zero || ttl > TimeSpan.FromDays(365));

    private static void ValidateSaga(DatabaseEngine database, ReadOnlySpan<byte> key, SagaRecord record)
    {
        if (record is null || record.Lane is null || record.Lane.Partition is null
            || record.CreatorPrincipalId is null || record.SagaId == Guid.Empty || record.Revision < 1
            || !Enum.IsDefined(record.Phase) || record.StateJson is null
            || (record.Deadline is null) != (record.Timeout is null)
            || record.Deadline is { } deadline && deadline.Offset != TimeSpan.Zero
            || record.Phase == SagaPhase.TimedOut && record.Deadline is null
            || record.Phase is SagaPhase.Completed or SagaPhase.Cancelled && record.Deadline is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord);
        }
        ValidateLane(record.Lane);
        JsonData.Identifier(record.CreatorPrincipalId);
        ValidateKey(key, DueWorkProtocol.SagaSpace, record.Lane, record.SagaId);
        _ = JsonData.Validate(record.StateJson, database.Limits);
        if (record.Timeout is { } timeout)
        {
            ValidateTimeout(database, record.Lane, timeout, record.Deadline!.Value);
        }
    }

    private static void ValidateTimeout(DatabaseEngine database, QueueLaneRef lane,
        SagaTimeoutDefinition timeout, DateTimeOffset deadline)
    {
        if (timeout.Queue is null || timeout.Queue.Partition is null || timeout.Queue.Partition != lane.Partition
            || timeout.PayloadJson is null || timeout.HeadersJson is null
            || timeout.TimeToLive is { } ttl && (ttl <= TimeSpan.Zero || ttl > TimeSpan.FromDays(365)))
        {
            throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord);
        }
        ValidateLane(timeout.Queue);
        if (timeout.OrderingKey is { } orderingKey)
        {
            JsonData.Identifier(orderingKey);
        }
        _ = JsonData.Validate(timeout.PayloadJson, database.Limits);
        _ = JsonData.Validate(timeout.HeadersJson, database.Limits);
        if (timeout.TimeToLive is { } duration)
        {
            try
            {
                _ = deadline.Add(duration);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord);
            }
        }
    }

    private static void ValidateLane(QueueLaneRef lane)
    {
        DatabaseEngine.ValidatePartition(lane.Partition);
        JsonData.Identifier(lane.Queue);
    }

    private static void ValidateKey(ReadOnlySpan<byte> key, string space, QueueLaneRef lane, Guid id)
    {
        var components = KeyCodec.Decode(key);
        var expected = new object?[] { space, lane.Partition.TenantId, lane.Partition.DatabaseId,
            lane.Partition.TransactionDomainId, lane.Partition.PartitionKey, lane.Queue,
            id.ToString(DueWorkFields.GuidFormat) };
        if (components.Length != expected.Length || !components.SequenceEqual(expected))
        {
            throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord);
        }
    }

    private static DateTimeOffset OccurrenceTime(RecurringScheduleDefinition definition, long ordinal)
    {
        try
        {
            var ticks = checked(definition.FirstDueAt.UtcDateTime.Ticks + checked(definition.Interval.Ticks * ordinal));
            return new(ticks, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord);
        }
    }

    private static void ValidateOccurrenceExpiry(RecurringScheduleDefinition definition, DateTimeOffset dueAt)
    {
        if (definition.MessageTimeToLive is not { } duration)
        {
            return;
        }
        try
        {
            _ = dueAt.Add(duration);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidRecord);
        }
    }
}
