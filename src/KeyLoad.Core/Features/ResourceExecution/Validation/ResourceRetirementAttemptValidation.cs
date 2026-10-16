using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Checks a closed-purpose source attempt and its original ordered receipts.</summary>
internal static class ResourceRetirementAttemptValidation
{
    internal static ResourceRetirementAttemptContextV1 Seal(ResourceRetirementControlV1 control)
        => new(ResourceRetirementAttemptPurpose.SealBarrier, control, null, null);

    internal static ResourceRetirementAttemptContextV1 Owner(ResourceRetirementPreparationControlV1 control,
        RegisteredPhysicalOwnerV1 receiver)
        => new(ResourceRetirementAttemptPurpose.OwnerReservation, null, control, receiver);

    internal static ResourceRetirementAttemptV1 Prepared(ResourceRetirementAttemptV1 attempt)
        => attempt with
        {
            Stage = ResourceRetirementAttemptStage.Prepared,
            OriginalFingerprint = null,
            TerminalReceipt = null,
            Error = null,
            SafeDetail = null
        };

    internal static void Require(ResourceRetirementAttemptV1 attempt, ResourceRetirementControlV1 control,
        DatabaseLimits limits) => Require(attempt, Seal(control), limits);

    internal static void Require(ResourceRetirementAttemptV1 attempt, ResourceRetirementAttemptContextV1 context,
        DatabaseLimits limits)
    {
        ResourceRetirementAttemptContextValidation.Require(context, limits);
        var originalPhase = ResourceRetirementCleanupAttemptScope.OriginalPhase(context);
        if (attempt is null || attempt.AttemptId == Guid.Empty || attempt.Ordinal <= ResourceRetirementProtocol.Empty
            || attempt.Ordinal > limits.MaxResults || attempt.PolicyEpoch < ResourceRetirementProtocol.Empty
            || attempt.ExpiresAt == default || !Enum.IsDefined(attempt.Stage)
            || attempt.OriginalCommandId != ResourceRetirementIdentity.AttemptCommandId(context, originalPhase, attempt.AttemptId))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        JsonData.Identifier(attempt.PrincipalId);
        Receipt(attempt.PrepareReceipt, context, ResourceRetirementPhase.PrepareAttempt,
            ResourceRetirementAttemptContextValidation.SourceOwner(context),
            ResourceRetirementIdentity.AttemptCommandId(context, ResourceRetirementPhase.PrepareAttempt, attempt.AttemptId), limits);
        Terminal(attempt, context, originalPhase, limits);
        ResourceRetirementValidation.Bound(attempt, limits);
    }

    private static void Terminal(ResourceRetirementAttemptV1 attempt, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementPhase phase, DatabaseLimits limits)
    {
        if (attempt.Stage == ResourceRetirementAttemptStage.Prepared)
        {
            if (attempt.OriginalFingerprint is not null || attempt.TerminalReceipt is not null
                || attempt.Error is not null || attempt.SafeDetail is not null)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            return;
        }
        if (attempt.Stage == ResourceRetirementAttemptStage.Completed)
        {
            if (!ResourceRetirementValidation.Digest(attempt.OriginalFingerprint!)
                || attempt.Error is not null || attempt.SafeDetail is not null)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            Receipt(attempt.TerminalReceipt!, context, phase,
                ResourceRetirementAttemptContextValidation.ExecutionOwner(context), attempt.OriginalCommandId, limits);
            return;
        }
        if (attempt.Stage == ResourceRetirementAttemptStage.Failed && attempt.OriginalFingerprint is null
            || attempt.Error is null || !RetainedFailure(attempt.Error.Value, context) || string.IsNullOrWhiteSpace(attempt.SafeDetail)
            || attempt.Stage == ResourceRetirementAttemptStage.Expired && attempt.Error != ErrorCode.CursorExpired
            || attempt.OriginalFingerprint is not null && !ResourceRetirementValidation.Digest(attempt.OriginalFingerprint)
            || attempt.TerminalReceipt is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    internal static void Receipt(ResourceRetirementPhaseReceiptV1 receipt, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementPhase phase, PhysicalShardRecord owner, Guid commandId, DatabaseLimits limits)
    {
        if (receipt is null || receipt.Version != ResourceRetirementProtocol.Version || receipt.Phase != phase
            || receipt.RetirementId != ResourceRetirementAttemptContextValidation.Request(context).RetirementId
            || receipt.CommandId != commandId || receipt.AppliedPosition <= ResourceRetirementProtocol.Empty
            || receipt.IntentDigest != ResourceRetirementAttemptContextValidation.IntentDigest(context)
            || !ResourceRetirementValidation.Same(receipt.Lifetime, ResourceRetirementAttemptContextValidation.Lifetime(context))
            || receipt.PhysicalOwner is null || !PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, owner))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementValidation.Bound(receipt, limits);
    }

    internal static void Witness(ResourceRetirementAttemptV1 attempt, ResourceRetirementControlV1 control,
        DatabaseLimits limits) => Witness(attempt, Seal(control), limits);

    internal static void Witness(ResourceRetirementAttemptV1 attempt, ResourceRetirementAttemptContextV1 context,
        DatabaseLimits limits)
    {
        Require(attempt, context, limits);
        if (attempt.Stage != ResourceRetirementAttemptStage.Prepared)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
    }

    internal static bool RetainedFailure(ErrorCode code, ResourceRetirementAttemptContextV1 context)
        => Enum.IsDefined(code) && (code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported
            or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome)
            || context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier && Recoverable(code));

    internal static bool Recoverable(ErrorCode code)
        => code is ErrorCode.Validation or ErrorCode.TokenInvalidated or ErrorCode.OwnershipLost
            or ErrorCode.Conflict or ErrorCode.RevisionConflict or ErrorCode.RecoveryRequired
            or ErrorCode.BudgetExceeded or ErrorCode.ResourceExhausted or ErrorCode.CursorExpired
            or ErrorCode.PermissionDenied or ErrorCode.NotFound;
}
