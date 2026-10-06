using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int SagaCommandsAdjacentElementOffset = 1;
    private const int SagaCommandsInitialSequence = 0;
    private const int SagaCommandsMinimumPositiveCount = 1;
    private const string SagaCommandsReceiptIdentitySeparator = "-";

    private const long MaximumTimeoutTtlTicks = RecurringSagaProtocol.MaximumMessageTimeToLiveTicks;

    internal MutationReceipt ApplyCompareExchangeSaga(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, CompareExchangeSaga request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScheduleScope(request.Lane, request.SagaId, partition);
        request = NormalizeSagaRequest(AuthorizeSagaWrite(tx, principal, request, partition), partition, now);
        var key = RecurringSagaStorage.SagaKey(request.Lane, request.SagaId);
        var existing = tx.GetRecord<SagaRecord>(key);
        RequireSagaCas(request.ExpectedRevision, request.Phase, existing, request.Lane, request.SagaId);
        if (existing is not null)
        {
            RequireSagaCreatorWrite(tx, existing, request.Timeout, now);
        }
        var next = NewSagaRecord(existing, request, principal.Id);
        var nextBytes = RecurringSagaStorage.SerializedBytes(next);
        var capacity = existing is null
            ? RecurringSagaStorage.Capacity(tx, request.Lane)
            : RecurringSagaStorage.RequireCapacity(tx, request.Lane);
        var updatedCapacity = existing is null
            ? RecurringSagaStorage.Add(capacity, nextBytes, Limits)
            : RecurringSagaStorage.Replace(capacity, RecurringSagaStorage.SerializedBytes(existing), nextBytes, Limits);
        tx.PutRecord(key, next);
        tx.PutRecord(RecurringSagaStorage.CapacityKey(request.Lane), updatedCapacity);
        return SagaReceipt(RecurringSagaProtocol.SagaReceipt, request.Lane, request.SagaId, next.Revision);
    }

    internal MutationReceipt ApplyExpireSaga(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, ExpireSaga request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScheduleScope(request.Lane, request.SagaId, partition);
        var record = RequireSaga(tx, request.Lane, request.SagaId);
        RequireSagaRevision(request.ExpectedRevision, record);
        if (record.Phase != SagaPhase.Waiting)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }
        if (record.Deadline is not { } deadline || record.Timeout is not { } timeout
            || now.ToUniversalTime() < deadline)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.NotDue);
        }
        _ = Resource(tx, partition, request.Lane.Queue, ResourceKind.WorkQueue);
        RequireSagaCreator(tx, record.Lane, record.SagaId, now, true);
        var destination = Resource(tx, partition, timeout.Queue.Queue, ResourceKind.WorkQueue);
        RequireScheduleWriteAuthority(principal, timeout.Queue, destination);
        var message = SagaTimeoutMessage(record, timeout, deadline);
        _ = Enqueue(tx, principal, partition, message, now);
        var next = record with { Revision = checked(record.Revision + SagaCommandsAdjacentElementOffset), Phase = SagaPhase.TimedOut };
        var capacity = RecurringSagaStorage.RequireCapacity(tx, request.Lane);
        var updatedCapacity = RecurringSagaStorage.Replace(capacity, RecurringSagaStorage.SerializedBytes(record),
            RecurringSagaStorage.SerializedBytes(next), Limits);
        tx.PutRecord(RecurringSagaStorage.SagaKey(request.Lane, request.SagaId), next);
        tx.PutRecord(RecurringSagaStorage.CapacityKey(request.Lane), updatedCapacity);
        return SagaReceipt(RecurringSagaProtocol.TimeoutReceipt, request.Lane, request.SagaId, next.Revision);
    }

    private CompareExchangeSaga AuthorizeSagaWrite(IKeyValueView view, PrincipalRecord principal,
        CompareExchangeSaga request, PartitionRef partition)
    {
        var source = Resource(view, partition, request.Lane.Queue, ResourceKind.WorkQueue);
        RequireScheduleCapability(principal, request.Lane);
        RequireQueueFieldWrites(principal, source);
        var normalized = NormalizeSagaRequest(request, partition, DateTimeOffset.MinValue);
        if (normalized.Timeout is { } timeout)
        {
            var destination = Resource(view, partition, timeout.Queue.Queue, ResourceKind.WorkQueue);
            RequireScheduleWriteAuthority(principal, timeout.Queue, destination);
        }
        return normalized;
    }

    private CompareExchangeSaga NormalizeSagaRequest(CompareExchangeSaga request, PartitionRef partition,
        DateTimeOffset now)
    {
        if (request.SagaId == Guid.Empty || request.ExpectedRevision < SagaCommandsInitialSequence || !Enum.IsDefined(request.Phase))
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
        var stateJson = JsonData.Validate(request.StateJson, Limits);
        if (request.Phase == SagaPhase.TimedOut)
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
        if (request.Phase is SagaPhase.Completed or SagaPhase.Cancelled)
        {
            if (request.Deadline is not null || request.Timeout is not null)
            {
                throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
            }
            return request with { StateJson = stateJson };
        }
        if ((request.Deadline is null) != (request.Timeout is null))
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
        var deadline = request.Deadline;
        if (deadline is { } deadlineValue && (deadlineValue.Offset != TimeSpan.Zero || deadlineValue <= now))
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
        if (request.Timeout is not { } timeout)
        {
            return request with { StateJson = stateJson };
        }
        ValidateTimeoutDefinition(timeout, request.Lane, partition, deadline!.Value);
        return request with
        {
            StateJson = stateJson,
            Timeout = NormalizeTimeoutDefinition(timeout)
        };
    }

    private SagaTimeoutDefinition NormalizeTimeoutDefinition(SagaTimeoutDefinition timeout)
    {
        JsonData.Identifier(timeout.Queue.Queue);
        var payload = JsonData.Validate(timeout.PayloadJson, Limits);
        var headers = JsonData.Validate(timeout.HeadersJson, Limits);
        if (timeout.OrderingKey is not null)
        {
            JsonData.Identifier(timeout.OrderingKey);
        }
        return timeout with { PayloadJson = payload, HeadersJson = headers };
    }

    private static void ValidateTimeoutDefinition(SagaTimeoutDefinition timeout, QueueLaneRef source,
        PartitionRef partition, DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(timeout);
        if (timeout.Queue is null || timeout.Queue.Partition != partition || timeout.Queue.Partition != source.Partition
            || timeout.TimeToLive is { } ttl && (ttl <= TimeSpan.Zero || ttl.Ticks > MaximumTimeoutTtlTicks))
        {
            throw Errors.Fail(ErrorCode.Validation, RecurringSagaProtocol.InvalidRequest);
        }
        _ = AddTtl(deadline, timeout.TimeToLive);
    }

    private void RequireSagaCas(long expectedRevision, SagaPhase nextPhase, SagaRecord? existing,
        QueueLaneRef lane, Guid sagaId)
    {
        if (expectedRevision < SagaCommandsInitialSequence || (existing is null ? expectedRevision != SagaCommandsInitialSequence : expectedRevision != existing.Revision))
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
        ValidateSagaRecord(existing, lane, sagaId);
        if (existing.Phase != SagaPhase.Waiting || nextPhase is not (SagaPhase.Waiting or SagaPhase.Completed or SagaPhase.Cancelled))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }
    }

    private static SagaRecord NewSagaRecord(SagaRecord? existing, CompareExchangeSaga request, string principalId)
    {
        var revision = existing is null ? SagaCommandsAdjacentElementOffset : checked(existing.Revision + SagaCommandsAdjacentElementOffset);
        var deadline = request.Phase == SagaPhase.Waiting ? request.Deadline : null;
        var timeout = request.Phase == SagaPhase.Waiting ? request.Timeout : null;
        return new(request.Lane, request.SagaId, existing?.CreatorPrincipalId ?? principalId,
            revision, request.Phase, request.StateJson, deadline, timeout);
    }

    private static void RequireSagaRevision(long expectedRevision, SagaRecord record)
    {
        if (expectedRevision < SagaCommandsMinimumPositiveCount || expectedRevision != record.Revision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RecurringSagaProtocol.RevisionConflict);
        }
    }

    private SagaRecord RequireSaga(IKeyValueView view, QueueLaneRef lane, Guid sagaId)
    {
        var record = view.GetRecord<SagaRecord>(RecurringSagaStorage.SagaKey(lane, sagaId))
            ?? throw Errors.Fail(ErrorCode.NotFound, RecurringSagaProtocol.MissingSaga);
        ValidateSagaRecord(record, lane, sagaId);
        return record;
    }

    private void ValidateSagaRecord(SagaRecord record, QueueLaneRef lane, Guid sagaId)
    {
        if (record.Lane is null || record.CreatorPrincipalId is null || record.SagaId != sagaId || record.Lane != lane
            || record.Revision < SagaCommandsMinimumPositiveCount || !Enum.IsDefined(record.Phase) || record.StateJson is null
            || (record.Deadline is null) != (record.Timeout is null)
            || record.Phase == SagaPhase.TimedOut && record.Deadline is null
            || record.Phase is SagaPhase.Completed or SagaPhase.Cancelled && record.Deadline is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, RecurringSagaProtocol.CorruptRecord);
        }
        _ = JsonData.Validate(record.StateJson, Limits);
        if (record.Timeout is { } timeout)
        {
            ValidateTimeoutDefinition(timeout, lane, lane.Partition, record.Deadline!.Value);
            _ = NormalizeTimeoutDefinition(timeout);
        }
    }

    private static EnqueueMessage SagaTimeoutMessage(SagaRecord record, SagaTimeoutDefinition timeout,
        DateTimeOffset deadline)
    {
        var messageId = string.Concat(RecurringSagaProtocol.SagaTimeoutPrefix,
            record.SagaId.ToString(RecurringSagaProtocol.IdentifierFormat), SagaCommandsReceiptIdentitySeparator,
            record.Revision.ToString(RecurringSagaProtocol.HexOrdinalFormat,
                System.Globalization.CultureInfo.InvariantCulture));
        return new(timeout.Queue.Queue, messageId, timeout.PayloadJson, timeout.HeadersJson,
            ExpiresAt: AddTtl(deadline, timeout.TimeToLive), OrderingKey: timeout.OrderingKey);
    }

    private static DateTimeOffset? AddTtl(DateTimeOffset instant, TimeSpan? ttl)
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

    private static MutationReceipt SagaReceipt(string kind, QueueLaneRef lane, Guid id, long revision)
        => new(kind, lane.Queue, id.ToString(RecurringSagaProtocol.IdentifierFormat), revision);
}
