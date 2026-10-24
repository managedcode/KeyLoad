using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Owns the RecurringScheduleValidation operation contract.</summary>
internal static class RecurringScheduleValidation
{
    internal static void ValidateScheduleRevision(this DatabaseEngine engine, long expectedRevision, RecurringScheduleRecord? existing, QueueLaneRef lane, Guid scheduleId)
    {
        if (expectedRevision < MessagingCommandsSchedules.RecurringScheduleCommandsInitialSequence || (existing is null ? expectedRevision != MessagingCommandsSchedules.RecurringScheduleCommandsInitialSequence : expectedRevision != existing.Revision))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }

        if (existing is not null)
        {
            engine.ValidateScheduleRecord(existing, lane, scheduleId);
        }
    }

    internal static void RequireScheduleRevision(long expectedRevision, long revision)
    {
        if (expectedRevision < MessagingCommandsSchedules.RecurringScheduleCommandsMinimumPositiveCount || expectedRevision != revision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }
    }

    internal static RecurringScheduleRecord RequireSchedule(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef lane, Guid scheduleId)
    {
        var record = view.GetRecord<RecurringScheduleRecord>(RecurringSagaStorage.ScheduleKey(lane, scheduleId)) ?? throw Errors.Fail(ErrorCode.NotFound, RecurringSagaProtocol.MissingSchedule);
        engine.ValidateScheduleRecord(record, lane, scheduleId);
        return record;
    }

    internal static void ValidateScheduleRecord(this DatabaseEngine engine, RecurringScheduleRecord record, QueueLaneRef lane, Guid scheduleId)
    {
        if (record.Lane is null || record.CreatorPrincipalId is null || record.Definition is null || record.Lane != lane || record.ScheduleId != scheduleId || record.Definition.Lane != lane || record.Definition.ScheduleId != scheduleId || record.Revision < MessagingCommandsSchedules.RecurringScheduleCommandsMinimumPositiveCount || record.Generation < MessagingCommandsSchedules.RecurringScheduleCommandsMinimumPositiveCount || record.NextOrdinal < MessagingCommandsSchedules.RecurringScheduleCommandsInitialSequence)
        {
            throw Errors.Fail(ErrorCode.Corruption, RecurringSagaProtocol.CorruptRecord);
        }

        _ = engine.NormalizeScheduleDefinition(record.Definition);
    }

    internal static RecurringScheduleDefinition NormalizeScheduleDefinition(this DatabaseEngine engine, RecurringScheduleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Lane is null || definition.ScheduleId == Guid.Empty || definition.FirstDueAt.Offset != TimeSpan.Zero || definition.TimeZone != RecurringSagaProtocol.UtcZone || definition.Interval.Ticks is < MessagingCommandsSchedules.MinimumIntervalTicks or > MessagingCommandsSchedules.MaximumIntervalTicks || definition.Misfire != RecurringMisfirePolicy.CatchUp || definition.MessageTimeToLive is { } ttl && (ttl <= TimeSpan.Zero || ttl.Ticks > RecurringSagaProtocol.MaximumMessageTimeToLiveTicks))
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }

        JsonData.Identifier(definition.Lane.Queue);
        var payload = JsonData.Validate(definition.PayloadJson, engine.Limits);
        var headers = JsonData.Validate(definition.HeadersJson, engine.Limits);
        if (definition.OrderingKey is not null)
        {
            JsonData.Identifier(definition.OrderingKey);
        }

        return definition with
        {
            PayloadJson = payload,
            HeadersJson = headers
        };
    }

    internal static void ValidateScheduleScope(QueueLaneRef lane, Guid scheduleId, PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(lane);
        ArgumentNullException.ThrowIfNull(partition);
        if (scheduleId == Guid.Empty || lane.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }

        JsonData.Identifier(lane.Queue);
    }
}
