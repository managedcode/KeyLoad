using System.Globalization;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsSchedules operations against the caller's database context.</summary>
public static class MessagingCommandsSchedules
{
    internal const int RecurringScheduleCommandsAdjacentElementOffset = 1;
    internal const int RecurringScheduleCommandsMinimumPositiveCount = 1;
    internal const int RecurringScheduleCommandsEmptyElementCount = 0;
    internal const int RecurringScheduleCommandsLastComponentFromEnd = 1;
    internal const int RecurringScheduleCommandsFirstElementIndex = 0;
    internal const int RecurringScheduleCommandsInitialSequence = 0;
    internal const string RecurringScheduleCommandsReceiptIdentitySeparator = "-";
    internal const long MinimumIntervalTicks = TimeSpan.TicksPerSecond;
    internal const long MaximumIntervalTicks = 365L * TimeSpan.TicksPerDay;
    internal static MutationReceipt ApplyConfigureRecurringSchedule(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, ConfigureRecurringSchedule request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        var definition = RecurringScheduleValidation.NormalizeScheduleDefinition(request.Definition);
        RecurringScheduleValidation.ValidateScheduleScope(definition.Lane, definition.ScheduleId, partition);
        var resource = engine.Resource(tx, partition, definition.Lane.Queue, ResourceKind.WorkQueue);
        engine.RequireScheduleWriteAuthority(principal, definition.Lane, resource);
        var key = RecurringSagaStorage.ScheduleKey(definition.Lane, definition.ScheduleId);
        var existing = tx.GetRecord<RecurringScheduleRecord>(key);
        RecurringScheduleValidation.ValidateScheduleRevision(request.ExpectedRevision, existing, definition.Lane, definition.ScheduleId);
        if (existing is not null)
        {
            engine.RequireScheduleCreatorWrite(tx, existing, resource, now);
        }

        var capacity = existing is null ? RecurringSagaStorage.Capacity(tx, definition.Lane) : RecurringSagaStorage.RequireCapacity(tx, definition.Lane);
        var next = MessagingCommandsSchedules.NewScheduleRecord(existing, definition, principal.Id);
        var nextBytes = RecurringSagaStorage.SerializedBytes(next);
        var updatedCapacity = existing is null ? RecurringSagaStorage.Add(capacity, nextBytes) : RecurringSagaStorage.Replace(capacity, RecurringSagaStorage.SerializedBytes(existing), nextBytes);
        tx.PutRecord(key, next);
        tx.PutRecord(RecurringSagaStorage.CapacityKey(definition.Lane), updatedCapacity);
        return MessagingCommandsSchedules.ScheduleReceipt(RecurringSagaProtocol.ScheduleReceipt, definition.Lane, definition.ScheduleId, next.Revision);
    }

    internal static MutationReceipt ApplyCancelRecurringSchedule(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, CancelRecurringSchedule request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        RecurringScheduleValidation.ValidateScheduleScope(request.Lane, request.ScheduleId, partition);
        var resource = engine.Resource(tx, partition, request.Lane.Queue, ResourceKind.WorkQueue);
        engine.RequireScheduleWriteAuthority(principal, request.Lane, resource);
        var record = RecurringScheduleValidation.RequireSchedule(tx, request.Lane, request.ScheduleId);
        RecurringScheduleValidation.RequireScheduleRevision(request.ExpectedRevision, record.Revision);
        engine.RequireScheduleCreatorManagement(tx, record, now);
        if (record.Cancelled)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }

        var next = record with
        {
            Revision = checked(record.Revision + MessagingCommandsSchedules.RecurringScheduleCommandsAdjacentElementOffset),
            Cancelled = true
        };
        var capacity = RecurringSagaStorage.RequireCapacity(tx, request.Lane);
        var updatedCapacity = RecurringSagaStorage.Replace(capacity, RecurringSagaStorage.SerializedBytes(record), RecurringSagaStorage.SerializedBytes(next));
        tx.PutRecord(RecurringSagaStorage.ScheduleKey(request.Lane, request.ScheduleId), next);
        tx.PutRecord(RecurringSagaStorage.CapacityKey(request.Lane), updatedCapacity);
        return MessagingCommandsSchedules.ScheduleReceipt(RecurringSagaProtocol.CancelReceipt, request.Lane, request.ScheduleId, next.Revision);
    }

    internal static MutationReceipt ApplyEmitRecurringOccurrences(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, EmitRecurringOccurrences request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        RecurringScheduleValidation.ValidateScheduleScope(request.Lane, request.ScheduleId, partition);
        if (request.MaxOccurrences < MessagingCommandsSchedules.RecurringScheduleCommandsMinimumPositiveCount || request.MaxOccurrences > engine.MessagingExecution.MaximumOccurrenceCatchUp)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }

        var resource = engine.Resource(tx, partition, request.Lane.Queue, ResourceKind.WorkQueue);
        engine.RequireScheduleWriteAuthority(principal, request.Lane, resource);
        var record = RecurringScheduleValidation.RequireSchedule(tx, request.Lane, request.ScheduleId);
        if (record.Generation != request.ExpectedGeneration)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RecurringSagaProtocol.GenerationChanged);
        }

        if (record.Cancelled)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }

        var due = MessagingCommandsSchedules.DueOccurrences(record, now, request.MaxOccurrences);
        if (due.Count == MessagingCommandsSchedules.RecurringScheduleCommandsEmptyElementCount)
        {
            return MessagingCommandsSchedules.ScheduleReceipt(RecurringSagaProtocol.EmitReceipt, request.Lane, request.ScheduleId, record.Revision);
        }

        var nextOrdinal = MessagingCommandsSchedules.NextOccurrenceOrdinal(due[^MessagingCommandsSchedules.RecurringScheduleCommandsLastComponentFromEnd].Ordinal);
        engine.EmitOccurrences(tx, principal, record, due, now);
        MessagingCommandsSchedules.UpdateScheduleOrdinal(tx, record, nextOrdinal);
        return MessagingCommandsSchedules.ScheduleReceipt(RecurringSagaProtocol.EmitReceipt, request.Lane, request.ScheduleId, record.Revision);
    }

    internal static List<(long Ordinal, DateTimeOffset DueAt)> DueOccurrences(RecurringScheduleRecord record, DateTimeOffset now, int maximum)
    {
        var due = new List<(long Ordinal, DateTimeOffset DueAt)>(maximum);
        for (var offset = MessagingCommandsSchedules.RecurringScheduleCommandsFirstElementIndex; offset < maximum; offset++)
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

            var dueAt = MessagingCommandsSchedules.OccurrenceDueAt(record.Definition, ordinal);
            if (dueAt > now)
            {
                break;
            }

            due.Add((ordinal, dueAt));
        }

        return due;
    }

    internal static void EmitOccurrences(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, RecurringScheduleRecord record, List<(long Ordinal, DateTimeOffset DueAt)> due, DateTimeOffset now)
    {
        engine.RequireScheduleCreator(tx, record.Lane, record.ScheduleId, now);
        foreach (var occurrence in due)
        {
            var message = MessagingCommandsSchedules.OccurrenceMessage(record, occurrence.Ordinal, occurrence.DueAt);
            _ = engine.Enqueue(tx, principal, record.Lane.Partition, message, now);
        }
    }

    internal static void UpdateScheduleOrdinal(IAtomicTransaction tx, RecurringScheduleRecord record, long nextOrdinal)
    {
        var next = record with
        {
            NextOrdinal = nextOrdinal
        };
        var capacity = RecurringSagaStorage.RequireCapacity(tx, record.Lane);
        var updated = RecurringSagaStorage.Replace(capacity, RecurringSagaStorage.SerializedBytes(record), RecurringSagaStorage.SerializedBytes(next));
        tx.PutRecord(RecurringSagaStorage.ScheduleKey(record.Lane, record.ScheduleId), next);
        tx.PutRecord(RecurringSagaStorage.CapacityKey(record.Lane), updated);
    }

    internal static MutationReceipt ScheduleReceipt(string kind, QueueLaneRef lane, Guid id, long revision) => new(kind, lane.Queue, id.ToString(RecurringSagaProtocol.IdentifierFormat), revision);
    internal static RecurringScheduleRecord NewScheduleRecord(RecurringScheduleRecord? existing, RecurringScheduleDefinition definition, string principalId)
    {
        var revision = existing is null ? MessagingCommandsSchedules.RecurringScheduleCommandsAdjacentElementOffset : checked(existing.Revision + MessagingCommandsSchedules.RecurringScheduleCommandsAdjacentElementOffset);
        var generation = existing is null ? MessagingCommandsSchedules.RecurringScheduleCommandsAdjacentElementOffset : checked(existing.Generation + MessagingCommandsSchedules.RecurringScheduleCommandsAdjacentElementOffset);
        return new(definition.Lane, definition.ScheduleId, existing?.CreatorPrincipalId ?? principalId, definition, revision, generation, MessagingCommandsSchedules.RecurringScheduleCommandsInitialSequence, false);
    }

    internal static DateTimeOffset OccurrenceDueAt(RecurringScheduleDefinition definition, long ordinal)
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

    internal static long NextOccurrenceOrdinal(long ordinal)
    {
        try
        {
            return checked(ordinal + MessagingCommandsSchedules.RecurringScheduleCommandsAdjacentElementOffset);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
    }

    internal static EnqueueMessage OccurrenceMessage(RecurringScheduleRecord record, long ordinal, DateTimeOffset dueAt)
    {
        var definition = record.Definition;
        var id = string.Concat(RecurringSagaProtocol.SchedulePrefix, record.ScheduleId.ToString(RecurringSagaProtocol.IdentifierFormat), MessagingCommandsSchedules.RecurringScheduleCommandsReceiptIdentitySeparator, record.Generation.ToString(RecurringSagaProtocol.HexOrdinalFormat, CultureInfo.InvariantCulture), MessagingCommandsSchedules.RecurringScheduleCommandsReceiptIdentitySeparator, ordinal.ToString(RecurringSagaProtocol.HexOrdinalFormat, CultureInfo.InvariantCulture));
        var expiresAt = (MessagingCommandsSchedules.OccurrenceExpiry(dueAt, definition.MessageTimeToLive)).Value;
        return new(definition.Lane.Queue, id, definition.PayloadJson, definition.HeadersJson, dueAt, expiresAt, definition.OrderingKey);
    }

    internal static (bool HasValue, DateTimeOffset? Value) OccurrenceExpiry(DateTimeOffset dueAt, TimeSpan? timeToLive)
    {
        if (timeToLive is not { } ttl)
        {
            return (false, default(DateTimeOffset?));
        }

        try
        {
            return (true, ((DateTimeOffset?)(dueAt.Add(ttl))));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
    }
}
