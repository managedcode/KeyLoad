using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Source-authoritative attempt history; target prepared bodies are stored separately.</summary>
internal static class ResourceRetirementAttemptStorage
{
    private const string Space = "resource-retirement-attempt-v1";
    private const string CurrentSpace = "resource-retirement-attempt-current-v1";

    private static byte[] Key(ResourceRetirementAttemptContextV1 context, Guid attemptId)
    {
        if (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
        {
            return ResourceRetirementCleanupAttemptIdentity.Key(Space, context, attemptId);
        }

        var lifetime = ResourceRetirementAttemptContextValidation.Lifetime(context);
        return KeyCodec.Encode(Space, lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName,
            lifetime.Incarnation.Value, ResourceRetirementAttemptContextValidation.Request(context).RetirementId,
            (int)context.Purpose, ResourceRetirementAttemptContextValidation.ExecutionOwner(context).PhysicalShardId, attemptId);
    }

    internal static ResourceRetirementAttemptV1? Read(IKeyValueView view, ResourceRetirementAttemptContextV1 context,
        Guid attemptId, DatabaseLimits limits, StorageReadObserver? observer = null)
    {
        ResourceRetirementAttemptContextValidation.Require(context, limits);
        if (attemptId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        var attempt = ResourceRetirementStorage.Read<ResourceRetirementAttemptV1>(view, Key(context, attemptId), limits, observer);
        if (attempt is not null)
        {
            ResourceRetirementAttemptValidation.Require(attempt, context, limits);
            ResourceRetirementAttemptEvidence.Require(view, context, attempt, limits, observer);
            if (attempt.AttemptId != attemptId)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
        return attempt;
    }

    internal static ResourceRetirementAttemptV1? Current(IKeyValueView view,
        ResourceRetirementAttemptContextV1 context, DatabaseLimits limits)
    {
        ResourceRetirementAttemptContextValidation.Require(context, limits);
        if (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
        {
            return CleanupCurrent(view, context, limits);
        }

        var lifetime = ResourceRetirementAttemptContextValidation.Lifetime(context);
        if (context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier)
        {
            var state = ResourceRetirementStorage.ReadControl(view, lifetime, limits)
                ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementProtocol.Missing);
            if (!ResourceRetirementValidation.Same(state.Control, context.Retirement))
            {
                throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
            }
            return state.SealAttempt;
        }
        var preparation = ResourceRetirementPreparationStorage.ReadPreparation(view, lifetime, limits)
            ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementProtocol.Missing);
        if (!ResourceRetirementValidation.Same(preparation.Control, context.Preparation))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        return preparation.OwnerAttempts.SingleOrDefault(attempt =>
            ResourceRetirementAttemptPointers.Belongs(attempt, context.Preparation!, context.Receiver!));
    }

    internal static ResourceRetirementAttemptV1 RequirePrepared(IKeyValueView view,
        ResourceRetirementControlV1 control, ResourceRetirementAttemptV1 expected, DatabaseLimits limits)
        => RequirePrepared(view, ResourceRetirementAttemptValidation.Seal(control), expected, limits);

    internal static ResourceRetirementAttemptV1 RequirePrepared(IKeyValueView view,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 expected, DatabaseLimits limits)
    {
        ResourceRetirementAttemptValidation.Witness(expected, context, limits);
        var current = Current(view, context, limits);
        var stored = Read(view, context, expected.AttemptId, limits)
            ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementProtocol.Missing);
        if (!ResourceRetirementValidation.Same(current, stored) || !ResourceRetirementValidation.Same(stored, expected))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        return stored;
    }

    internal static void ValidatePointer(IKeyValueView view, ResourceRetirementStateV1 state, DatabaseLimits limits)
    {
        if (state.SealAttempt is not null && !ResourceRetirementValidation.Same(state.SealAttempt,
            Read(view, ResourceRetirementAttemptValidation.Seal(state.Control), state.SealAttempt.AttemptId, limits)))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    internal static void Write(IAtomicTransaction transaction, ResourceRetirementControlV1 control,
        ResourceRetirementAttemptV1 attempt, DatabaseLimits limits)
        => Write(transaction, ResourceRetirementAttemptValidation.Seal(control), attempt, limits);

    internal static void Write(IAtomicTransaction transaction, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, DatabaseLimits limits)
    {
        ResourceRetirementAttemptValidation.Require(attempt, context, limits);
        var previous = Read(transaction, context, attempt.AttemptId, limits);
        if (previous is not null && (!ResourceRetirementValidation.Same(previous with
        {
            Stage = attempt.Stage,
            OriginalFingerprint = attempt.OriginalFingerprint,
            TerminalReceipt = attempt.TerminalReceipt,
            Error = attempt.Error,
            SafeDetail = attempt.SafeDetail
        }, attempt)
            || previous.Stage != ResourceRetirementAttemptStage.Prepared))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        transaction.PutRecord(Key(context, attempt.AttemptId), attempt);
    }

    private static ResourceRetirementAttemptV1? CleanupCurrent(IKeyValueView view,
        ResourceRetirementAttemptContextV1 context, DatabaseLimits limits)
    {
        var current = ResourceRetirementStorage.Read<ResourceRetirementAttemptV1>(view,
            ResourceRetirementCleanupAttemptIdentity.Key(CurrentSpace, context), limits);
        if (current is null)
        {
            return null;
        }

        var stored = Read(view, context, current.AttemptId, limits);
        if (!ResourceRetirementValidation.Same(current, stored))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        return current;
    }

    internal static void WriteCleanupCurrent(IAtomicTransaction tx, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, DatabaseLimits limits)
    {
        ResourceRetirementAttemptContextValidation.Require(context, limits);
        ResourceRetirementAttemptValidation.Witness(attempt, context, limits);
        if (context.Purpose != ResourceRetirementAttemptPurpose.CleanupPage)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        tx.PutRecord(ResourceRetirementCleanupAttemptIdentity.Key(CurrentSpace, context), attempt);
    }
}
