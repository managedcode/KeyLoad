using System.Globalization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int MaximumOccurrenceCatchUp = 32;
    private const long MinimumIntervalTicks = TimeSpan.TicksPerSecond;
    private const long MaximumIntervalTicks = 365L * TimeSpan.TicksPerDay;

    internal MutationReceipt ApplyConfigureRecurringSchedule(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, ConfigureRecurringSchedule request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        var definition = NormalizeScheduleDefinition(request.Definition);
        ValidateScheduleScope(definition.Lane, definition.ScheduleId, partition);
        var resource = Resource(tx, partition, definition.Lane.Queue, ResourceKind.WorkQueue);
        RequireScheduleWriteAuthority(principal, definition.Lane, resource);
        var key = RecurringSagaStorage.ScheduleKey(definition.Lane, definition.ScheduleId);
        var existing = tx.GetRecord<RecurringScheduleRecord>(key);
        ValidateScheduleRevision(request.ExpectedRevision, existing, definition.Lane, definition.ScheduleId);
        if (existing is not null)
        {
            RequireScheduleCreatorWrite(tx, existing, resource, now);
        }
        var capacity = existing is null
            ? RecurringSagaStorage.Capacity(tx, definition.Lane)
            : RecurringSagaStorage.RequireCapacity(tx, definition.Lane);
        var next = NewScheduleRecord(existing, definition, principal.Id);
        var nextBytes = RecurringSagaStorage.SerializedBytes(next);
        var updatedCapacity = existing is null
            ? RecurringSagaStorage.Add(capacity, nextBytes, Limits)
            : RecurringSagaStorage.Replace(capacity, RecurringSagaStorage.SerializedBytes(existing), nextBytes, Limits);
        tx.PutRecord(key, next);
        tx.PutRecord(RecurringSagaStorage.CapacityKey(definition.Lane), updatedCapacity);
        return ScheduleReceipt(RecurringSagaProtocol.ScheduleReceipt, definition.Lane, definition.ScheduleId, next.Revision);
    }

    internal MutationReceipt ApplyCancelRecurringSchedule(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, CancelRecurringSchedule request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScheduleScope(request.Lane, request.ScheduleId, partition);
        var resource = Resource(tx, partition, request.Lane.Queue, ResourceKind.WorkQueue);
        RequireScheduleWriteAuthority(principal, request.Lane, resource);
        var record = RequireSchedule(tx, request.Lane, request.ScheduleId);
        RequireScheduleRevision(request.ExpectedRevision, record.Revision);
        RequireScheduleCreatorManagement(tx, record, now);
        if (record.Cancelled)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }
        var next = record with { Revision = checked(record.Revision + 1), Cancelled = true };
        var capacity = RecurringSagaStorage.RequireCapacity(tx, request.Lane);
        var updatedCapacity = RecurringSagaStorage.Replace(capacity, RecurringSagaStorage.SerializedBytes(record),
            RecurringSagaStorage.SerializedBytes(next), Limits);
        tx.PutRecord(RecurringSagaStorage.ScheduleKey(request.Lane, request.ScheduleId), next);
        tx.PutRecord(RecurringSagaStorage.CapacityKey(request.Lane), updatedCapacity);
        return ScheduleReceipt(RecurringSagaProtocol.CancelReceipt, request.Lane, request.ScheduleId, next.Revision);
    }

    internal MutationReceipt ApplyEmitRecurringOccurrences(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, EmitRecurringOccurrences request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScheduleScope(request.Lane, request.ScheduleId, partition);
        if (request.MaxOccurrences is < 1 or > MaximumOccurrenceCatchUp)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
        var resource = Resource(tx, partition, request.Lane.Queue, ResourceKind.WorkQueue);
        RequireScheduleWriteAuthority(principal, request.Lane, resource);
        var record = RequireSchedule(tx, request.Lane, request.ScheduleId);
        if (record.Generation != request.ExpectedGeneration)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RecurringSagaProtocol.GenerationChanged);
        }
        if (record.Cancelled)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }
        var due = DueOccurrences(record, now, request.MaxOccurrences);
        if (due.Count == 0)
        {
            return ScheduleReceipt(RecurringSagaProtocol.EmitReceipt, request.Lane, request.ScheduleId, record.Revision);
        }
        var nextOrdinal = NextOccurrenceOrdinal(due[^1].Ordinal);
        EmitOccurrences(tx, principal, record, due, now);
        UpdateScheduleOrdinal(tx, record, nextOrdinal);
        return ScheduleReceipt(RecurringSagaProtocol.EmitReceipt, request.Lane, request.ScheduleId, record.Revision);
    }

    private static List<(long Ordinal, DateTimeOffset DueAt)> DueOccurrences(RecurringScheduleRecord record,
        DateTimeOffset now, int maximum)
    {
        var due = new List<(long Ordinal, DateTimeOffset DueAt)>(maximum);
        for (var offset = 0; offset < maximum; offset++)
        {
            long ordinal;
            try
            {
                ordinal = checked(record.NextOrdinal + offset);
            }
            catch (OverflowException)
            {
                throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
            }
            var dueAt = OccurrenceDueAt(record.Definition, ordinal);
            if (dueAt > now)
            {
                break;
            }
            due.Add((ordinal, dueAt));
        }
        return due;
    }

    private void EmitOccurrences(IAtomicTransaction tx, PrincipalRecord principal, RecurringScheduleRecord record,
        List<(long Ordinal, DateTimeOffset DueAt)> due, DateTimeOffset now)
    {
        RequireScheduleCreator(tx, record.Lane, record.ScheduleId, now);
        foreach (var occurrence in due)
        {
            var message = OccurrenceMessage(record, occurrence.Ordinal, occurrence.DueAt);
            _ = Enqueue(tx, principal, record.Lane.Partition, message, now);
        }
    }

    private void UpdateScheduleOrdinal(IAtomicTransaction tx, RecurringScheduleRecord record, long nextOrdinal)
    {
        var next = record with { NextOrdinal = nextOrdinal };
        var capacity = RecurringSagaStorage.RequireCapacity(tx, record.Lane);
        var updated = RecurringSagaStorage.Replace(capacity, RecurringSagaStorage.SerializedBytes(record),
            RecurringSagaStorage.SerializedBytes(next), Limits);
        tx.PutRecord(RecurringSagaStorage.ScheduleKey(record.Lane, record.ScheduleId), next);
        tx.PutRecord(RecurringSagaStorage.CapacityKey(record.Lane), updated);
    }

    private static MutationReceipt ScheduleReceipt(string kind, QueueLaneRef lane, Guid id, long revision)
        => new(kind, lane.Queue, id.ToString(RecurringSagaProtocol.IdentifierFormat), revision);

    private static RecurringScheduleRecord NewScheduleRecord(RecurringScheduleRecord? existing,
        RecurringScheduleDefinition definition, string principalId)
    {
        var revision = existing is null ? 1 : checked(existing.Revision + 1);
        var generation = existing is null ? 1 : checked(existing.Generation + 1);
        return new(definition.Lane, definition.ScheduleId, existing?.CreatorPrincipalId ?? principalId,
            definition, revision, generation, 0, false);
    }

    private void ValidateScheduleRevision(long expectedRevision, RecurringScheduleRecord? existing,
        QueueLaneRef lane, Guid scheduleId)
    {
        if (expectedRevision < 0 || (existing is null ? expectedRevision != 0 : expectedRevision != existing.Revision))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }
        if (existing is not null)
        {
            ValidateScheduleRecord(existing, lane, scheduleId);
        }
    }

    private static void RequireScheduleRevision(long expectedRevision, long revision)
    {
        if (expectedRevision < 1 || expectedRevision != revision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }
    }

    private RecurringScheduleRecord RequireSchedule(IKeyValueView view, QueueLaneRef lane, Guid scheduleId)
    {
        var record = view.GetRecord<RecurringScheduleRecord>(RecurringSagaStorage.ScheduleKey(lane, scheduleId))
            ?? throw Errors.Fail(ErrorCode.NotFound, RecurringSagaProtocol.MissingSchedule);
        ValidateScheduleRecord(record, lane, scheduleId);
        return record;
    }

    private void ValidateScheduleRecord(RecurringScheduleRecord record, QueueLaneRef lane, Guid scheduleId)
    {
        if (record.Lane is null || record.CreatorPrincipalId is null || record.Definition is null
            || record.Lane != lane || record.ScheduleId != scheduleId || record.Definition.Lane != lane
            || record.Definition.ScheduleId != scheduleId || record.Revision < 1 || record.Generation < 1
            || record.NextOrdinal < 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, RecurringSagaProtocol.CorruptRecord);
        }
        _ = NormalizeScheduleDefinition(record.Definition);
    }

    private RecurringScheduleDefinition NormalizeScheduleDefinition(RecurringScheduleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Lane is null || definition.ScheduleId == Guid.Empty || definition.FirstDueAt.Offset != TimeSpan.Zero
            || definition.TimeZone != RecurringSagaProtocol.UtcZone || definition.Interval.Ticks is < MinimumIntervalTicks or > MaximumIntervalTicks
            || definition.Misfire != RecurringMisfirePolicy.CatchUp
            || definition.MessageTimeToLive is { } ttl && (ttl <= TimeSpan.Zero || ttl.Ticks > RecurringSagaProtocol.MaximumMessageTimeToLiveTicks))
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
        JsonData.Identifier(definition.Lane.Queue);
        var payload = JsonData.Validate(definition.PayloadJson, Limits);
        var headers = JsonData.Validate(definition.HeadersJson, Limits);
        if (definition.OrderingKey is not null)
        {
            JsonData.Identifier(definition.OrderingKey);
        }
        return definition with { PayloadJson = payload, HeadersJson = headers };
    }

    private static DateTimeOffset OccurrenceDueAt(RecurringScheduleDefinition definition, long ordinal)
    {
        try
        {
            var dueTicks = checked(definition.FirstDueAt.UtcDateTime.Ticks + checked(definition.Interval.Ticks * ordinal));
            return new DateTimeOffset(dueTicks, TimeSpan.Zero);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
    }

    private static long NextOccurrenceOrdinal(long ordinal)
    {
        try
        {
            return checked(ordinal + 1);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
    }

    private static EnqueueMessage OccurrenceMessage(RecurringScheduleRecord record, long ordinal, DateTimeOffset dueAt)
    {
        var definition = record.Definition;
        var id = string.Concat(RecurringSagaProtocol.SchedulePrefix,
            record.ScheduleId.ToString(RecurringSagaProtocol.IdentifierFormat), "-",
            record.Generation.ToString(RecurringSagaProtocol.HexOrdinalFormat, CultureInfo.InvariantCulture), "-",
            ordinal.ToString(RecurringSagaProtocol.HexOrdinalFormat, CultureInfo.InvariantCulture));
        var expiresAt = OccurrenceExpiry(dueAt, definition.MessageTimeToLive);
        return new(definition.Lane.Queue, id, definition.PayloadJson, definition.HeadersJson,
            dueAt, expiresAt, definition.OrderingKey);
    }

    private static DateTimeOffset? OccurrenceExpiry(DateTimeOffset dueAt, TimeSpan? timeToLive)
    {
        if (timeToLive is not { } ttl)
        {
            return null;
        }
        try
        {
            return dueAt.Add(ttl);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
    }

    private static void ValidateScheduleScope(QueueLaneRef lane, Guid scheduleId, PartitionRef partition)
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
