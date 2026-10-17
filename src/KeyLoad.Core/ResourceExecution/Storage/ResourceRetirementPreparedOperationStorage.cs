using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementPreparedOperationStorage
{
    private const string Space = "resource-retirement-prepared-operation-v1";

    private static byte[] Key(ResourceRetirementAttemptContextV1 context, Guid attemptId)
    {
        if (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
        {
            return ResourceRetirementCleanupAttemptIdentity.Key(Space, context, attemptId);
        }

        var request = ResourceRetirementAttemptContextValidation.Request(context);
        var owner = ResourceRetirementAttemptContextValidation.ExecutionOwner(context);
        return KeyCodec.Encode(Space, request.TenantId, request.DatabaseId, request.ResourceName,
            request.ExpectedIncarnation.Value, request.RetirementId, (int)context.Purpose,
            owner.PhysicalShardId, owner.Incarnation, attemptId);
    }

    internal static ResourceRetirementPreparedOperationV1? Read(IKeyValueView view,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt,
        DatabaseLimits limits, StorageReadObserver? observer = null)
    {
        ResourceRetirementAttemptContextValidation.Require(context, limits);
        if (attempt is null)
        {
            throw ResourceRetirementPreparedOperationValidation.Invalid();
        }
        ResourceRetirementAttemptValidation.Witness(Prepared(attempt), context, limits);
        var prepared = ReadRecord<ResourceRetirementPreparedOperationV1>(view, Key(context, attempt.AttemptId), limits, observer);
        if (prepared is null)
        {
            return null;
        }
        ResourceRetirementPreparedOperationValidation.Require(prepared, context, attempt, limits);
        if (context.Purpose == ResourceRetirementAttemptPurpose.OwnerReservation)
        {
            ResourceRetirementPreparedReplacementAccounting.RequireRow(view, context, limits, observer);
        }
        RequireOutcome(view, prepared, limits, observer);
        if (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage
            && prepared.Stage == ResourceRetirementPreparedOperationStage.Closed && prepared.Original is not null)
        {
            ResourceRetirementCleanupPreparedEvidence.RequireFailed(ReadOriginalOutcome(view, prepared, limits, observer), prepared, limits);
        }

        if (prepared.Stage == ResourceRetirementPreparedOperationStage.Closed && prepared.PersistReceipt is not null)
        {
            RequireOutcome(view, prepared with
            {
                Stage = ResourceRetirementPreparedOperationStage.Persisted,
                Receipt = prepared.PersistReceipt,
                ReceiptAuthority = prepared.PersistAuthority,
                Error = null,
                SafeDetail = null
            }, limits, observer);
        }
        return prepared;
    }

    internal static ResourceRetirementPreparedOperationV1 ReadCleanupCheckpoint(IKeyValueView view,
        ResourceRetirementCleanupCheckpointV1 checkpoint, DatabaseLimits limits)
    {
        var context = ResourceRetirementCleanupAttemptScope.Context(checkpoint.Control, checkpoint.Receiver,
            checkpoint.Partition, checkpoint.Ordinal);
        ResourceRetirementAttemptContextValidation.Require(context, limits);
        var raw = ResourceRetirementStorage.Read<ResourceRetirementPreparedOperationV1>(view,
            Key(context, checkpoint.AttemptId), limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        if (raw.Attempt is null || raw.Attempt.AttemptId != checkpoint.AttemptId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        return Read(view, context, raw.Attempt, limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
    }

    internal static ResourceRetirementAttemptV1 Prepared(ResourceRetirementAttemptV1 attempt)
    {
        if (attempt is null)
        {
            throw ResourceRetirementPreparedOperationValidation.Invalid();
        }
        return attempt with
        {
            Stage = ResourceRetirementAttemptStage.Prepared,
            OriginalFingerprint = null,
            TerminalReceipt = null,
            Error = null,
            SafeDetail = null
        };
    }

    internal static bool Exact(ReplicatedOperation left, ReplicatedOperation right)
        => left.Id == right.Id && left.Kind == right.Kind && left.PrincipalId == right.PrincipalId
            && left.EvaluatedAt.EqualsExact(right.EvaluatedAt) && left.PayloadJson == right.PayloadJson
            && left.NativePayload.Span.SequenceEqual(right.NativePayload.Span);

    internal static void RequireClosed(ResourceRetirementPreparedOperationV1 witness,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 previous, DatabaseLimits limits)
    {
        ResourceRetirementPreparedOperationValidation.Require(witness, context, previous, limits);
        if (witness.Stage != ResourceRetirementPreparedOperationStage.Closed)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        }
    }

    internal static void Write(IAtomicTransaction transaction, ResourceRetirementPreparedOperationV1 prepared,
        DatabaseLimits limits)
    {
        ResourceRetirementPreparedOperationValidation.Require(prepared, prepared.Context, prepared.Attempt, limits);
        var previous = Read(transaction, prepared.Context, prepared.Attempt, limits);
        if (previous is not null && (previous.Stage != ResourceRetirementPreparedOperationStage.Persisted
            || prepared.Stage != ResourceRetirementPreparedOperationStage.Closed
            || previous.Original is null || prepared.Original is null
            || !Exact(previous.Original, prepared.Original)
            || !ResourceRetirementValidation.Same(previous.PersistReceipt, prepared.PersistReceipt)
            || !ResourceRetirementValidation.Same(previous.PersistAuthority, prepared.PersistAuthority)))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        if (previous is null)
        {
            ResourceRetirementPreparedReplacementAccounting.RecordNew(transaction, prepared.Context, prepared.Attempt, limits);
        }
        transaction.PutRecord(Key(prepared.Context, prepared.Attempt.AttemptId), prepared);
    }

    internal static void RequireNoReplacement(IKeyValueView view, DatabaseLimits limits)
        => ResourceRetirementPreparedReplacementAccounting.RequireNoReplacement(view, limits);

    internal static long ReadReplacementLifeCount(IKeyValueView view, ResourceLifetimeV1 lifetime,
        PhysicalShardRecord owner, DatabaseLimits limits, StorageReadObserver? observer = null)
        => ResourceRetirementPreparedReplacementAccounting.ReadLife(view, lifetime, owner, limits, observer);

    internal static void ReleaseReplacementBlockers(IAtomicTransaction transaction, ResourceLifetimeV1 lifetime,
        PhysicalShardRecord owner, DatabaseLimits limits)
        => ResourceRetirementPreparedReplacementAccounting.Release(transaction, lifetime, owner, limits);

    internal static StoredOutcome? ReadOriginalOutcome(IKeyValueView view,
        ResourceRetirementPreparedOperationV1 prepared, DatabaseLimits limits, StorageReadObserver? observer = null)
        => prepared.Original is null ? null : ReadRecord<StoredOutcome>(view,
            KeySpace.GlobalOutcome(prepared.Original.PrincipalId, prepared.Original.Id), limits, observer);

    private static void RequireOutcome(IKeyValueView view, ResourceRetirementPreparedOperationV1 prepared,
        DatabaseLimits limits, StorageReadObserver? observer)
    {
        var authority = prepared.ReceiptAuthority!;
        var outcome = ReadRecord<StoredOutcome>(view,
            KeySpace.GlobalOutcome(authority.PrincipalId, prepared.Receipt.CommandId), limits, observer);
        ResourceRetirementPreparedOperationValidation.Outcome(outcome, prepared, limits);
    }

    private static T? ReadRecord<T>(IKeyValueView view, byte[] key, DatabaseLimits limits,
        StorageReadObserver? observer) where T : class
    {
        T? result = null;
        view.ReadValue(key, bytes =>
        {
            if (bytes.Length > limits.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            result = NativeSerialization.Deserialize<T>(bytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }, observer);
        return result;
    }
}
