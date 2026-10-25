using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes SagaCommands operations against the caller's database context.</summary>
public static class SagaCommands
{
    internal const int SagaCommandsAdjacentElementOffset = 1;
    internal const int SagaCommandsInitialSequence = 0;
    internal const int SagaCommandsMinimumPositiveCount = 1;
    internal const string SagaCommandsReceiptIdentitySeparator = "-";
    internal const long MaximumTimeoutTtlTicks = RecurringSagaProtocol.MaximumMessageTimeToLiveTicks;
    internal static MutationReceipt ApplyCompareExchangeSaga(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, CompareExchangeSaga request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        RecurringScheduleValidation.ValidateScheduleScope(request.Lane, request.SagaId, partition);
        request = SagaDefinitionValidation.NormalizeSagaRequest(engine.AuthorizeSagaWrite(tx, principal, request, partition), partition, now);
        var key = RecurringSagaStorage.SagaKey(request.Lane, request.SagaId);
        var existing = tx.GetRecord<SagaRecord>(key);
        SagaCommands.RequireSagaCas(request.ExpectedRevision, request.Phase, existing, request.Lane, request.SagaId);
        if (existing is not null)
        {
            engine.RequireSagaCreatorWrite(tx, existing, request.Timeout, now);
        }

        var next = SagaCommands.NewSagaRecord(existing, request, principal.Id);
        var nextBytes = RecurringSagaStorage.SerializedBytes(next);
        var capacity = existing is null ? RecurringSagaStorage.Capacity(tx, request.Lane) : RecurringSagaStorage.RequireCapacity(tx, request.Lane);
        var updatedCapacity = existing is null ? RecurringSagaStorage.Add(capacity, nextBytes) : RecurringSagaStorage.Replace(capacity, RecurringSagaStorage.SerializedBytes(existing), nextBytes);
        tx.PutRecord(key, next);
        tx.PutRecord(RecurringSagaStorage.CapacityKey(request.Lane), updatedCapacity);
        return SagaCommands.SagaReceipt(RecurringSagaProtocol.SagaReceipt, request.Lane, request.SagaId, next.Revision);
    }

    internal static MutationReceipt ApplyExpireSaga(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, ExpireSaga request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        RecurringScheduleValidation.ValidateScheduleScope(request.Lane, request.SagaId, partition);
        var record = SagaCommands.RequireSaga(tx, request.Lane, request.SagaId);
        SagaCommands.RequireSagaRevision(request.ExpectedRevision, record);
        if (record.Phase != SagaPhase.Waiting)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }

        if (record.Deadline is not { } deadline || record.Timeout is not { } timeout || now.ToUniversalTime() < deadline)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.NotDue);
        }

        _ = engine.Resource(tx, partition, request.Lane.Queue, ResourceKind.WorkQueue);
        engine.RequireSagaCreator(tx, record.Lane, record.SagaId, now, true);
        engine.RequireScheduleCapability(principal, timeout.Queue);
        var destination = engine.Resource(tx, partition, timeout.Queue.Queue, ResourceKind.WorkQueue);
        engine.RequireScheduleWriteAuthority(principal, timeout.Queue, destination);
        var message = SagaCommands.SagaTimeoutMessage(record, timeout, deadline);
        _ = engine.Enqueue(tx, principal, partition, message, now);
        var next = record with
        {
            Revision = checked(record.Revision + SagaCommands.SagaCommandsAdjacentElementOffset),
            Phase = SagaPhase.TimedOut
        };
        var capacity = RecurringSagaStorage.RequireCapacity(tx, request.Lane);
        var updatedCapacity = RecurringSagaStorage.Replace(capacity, RecurringSagaStorage.SerializedBytes(record), RecurringSagaStorage.SerializedBytes(next));
        tx.PutRecord(RecurringSagaStorage.SagaKey(request.Lane, request.SagaId), next);
        tx.PutRecord(RecurringSagaStorage.CapacityKey(request.Lane), updatedCapacity);
        return SagaCommands.SagaReceipt(RecurringSagaProtocol.TimeoutReceipt, request.Lane, request.SagaId, next.Revision);
    }

    internal static CompareExchangeSaga AuthorizeSagaWrite(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, CompareExchangeSaga request, PartitionRef partition)
    {
        engine.RequireScheduleCapability(principal, request.Lane);
        var source = engine.Resource(view, partition, request.Lane.Queue, ResourceKind.WorkQueue);
        engine.RequireQueueFieldWrites(principal, source);
        var normalized = SagaDefinitionValidation.NormalizeSagaRequest(request, partition, DateTimeOffset.MinValue);
        if (normalized.Timeout is { } timeout)
        {
            engine.RequireScheduleCapability(principal, timeout.Queue);
            var destination = engine.Resource(view, partition, timeout.Queue.Queue, ResourceKind.WorkQueue);
            engine.RequireScheduleWriteAuthority(principal, timeout.Queue, destination);
        }

        return normalized;
    }

    internal static void RequireSagaCas(long expectedRevision, SagaPhase nextPhase, SagaRecord? existing, QueueLaneRef lane, Guid sagaId)
    {
        if (expectedRevision < SagaCommands.SagaCommandsInitialSequence || (existing is null ? expectedRevision != SagaCommands.SagaCommandsInitialSequence : expectedRevision != existing.Revision))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }

        if (existing is null)
        {
            if (nextPhase != SagaPhase.Waiting)
            {
                throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
            }

            return;
        }

        SagaCommands.ValidateSagaRecord(existing, lane, sagaId);
        if (existing.Phase != SagaPhase.Waiting || nextPhase is not (SagaPhase.Waiting or SagaPhase.Completed or SagaPhase.Cancelled))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }
    }

    internal static SagaRecord NewSagaRecord(SagaRecord? existing, CompareExchangeSaga request, string principalId)
    {
        var revision = existing is null ? SagaCommands.SagaCommandsAdjacentElementOffset : checked(existing.Revision + SagaCommands.SagaCommandsAdjacentElementOffset);
        var deadline = request.Phase == SagaPhase.Waiting ? request.Deadline : null;
        var timeout = request.Phase == SagaPhase.Waiting ? request.Timeout : null;
        return new(request.Lane, request.SagaId, existing?.CreatorPrincipalId ?? principalId, revision, request.Phase, request.StateJson, deadline, timeout);
    }

    internal static void RequireSagaRevision(long expectedRevision, SagaRecord record)
    {
        if (expectedRevision < SagaCommands.SagaCommandsMinimumPositiveCount || expectedRevision != record.Revision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }
    }

    internal static SagaRecord RequireSaga(IKeyValueView view, QueueLaneRef lane, Guid sagaId)
    {
        var record = view.GetRecord<SagaRecord>(RecurringSagaStorage.SagaKey(lane, sagaId)) ?? throw Errors.Fail(ErrorCode.NotFound, RecurringSagaProtocol.MissingSaga);
        SagaCommands.ValidateSagaRecord(record, lane, sagaId);
        return record;
    }

    internal static void ValidateSagaRecord(SagaRecord record, QueueLaneRef lane, Guid sagaId)
    {
        if (record.Lane is null || record.CreatorPrincipalId is null || record.SagaId != sagaId || record.Lane != lane || record.Revision < SagaCommands.SagaCommandsMinimumPositiveCount || !Enum.IsDefined(record.Phase) || record.StateJson is null || (record.Deadline is null) != (record.Timeout is null) || record.Phase == SagaPhase.TimedOut && record.Deadline is null || record.Phase is SagaPhase.Completed or SagaPhase.Cancelled && record.Deadline is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, RecurringSagaProtocol.CorruptRecord);
        }

        _ = JsonData.Validate(record.StateJson);
        if (record.Timeout is { } timeout)
        {
            SagaDefinitionValidation.ValidateTimeoutDefinition(timeout, lane, lane.Partition, record.Deadline!.Value);
            _ = SagaDefinitionValidation.NormalizeTimeoutDefinition(timeout);
        }
    }

    internal static EnqueueMessage SagaTimeoutMessage(SagaRecord record, SagaTimeoutDefinition timeout, DateTimeOffset deadline)
    {
        var messageId = string.Concat(RecurringSagaProtocol.SagaTimeoutPrefix, record.SagaId.ToString(RecurringSagaProtocol.IdentifierFormat), SagaCommands.SagaCommandsReceiptIdentitySeparator, record.Revision.ToString(RecurringSagaProtocol.HexOrdinalFormat, System.Globalization.CultureInfo.InvariantCulture));
        return new(timeout.Queue.Queue, messageId, timeout.PayloadJson, timeout.HeadersJson, ExpiresAt: SagaCommands.AddTtl(deadline, timeout.TimeToLive), OrderingKey: timeout.OrderingKey);
    }

    internal static DateTimeOffset? AddTtl(DateTimeOffset instant, TimeSpan? ttl)
    {
        if (ttl is not { } duration)
        {
            return null;
        }

        try
        {
            return instant.Add(duration);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
    }

    internal static MutationReceipt SagaReceipt(string kind, QueueLaneRef lane, Guid id, long revision) => new(kind, lane.Queue, id.ToString(RecurringSagaProtocol.IdentifierFormat), revision);
}
