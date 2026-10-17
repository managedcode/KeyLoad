using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementPreparedOperationValidation
{
    private const int DigestCharacters = 64;
    private const char DigitFirst = '0';
    private const char DigitLast = '9';
    private const char HexFirst = 'a';
    private const char HexLast = 'f';

    internal static void Require(ResourceRetirementPreparedOperationV1 value,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt, DatabaseLimits limits)
    {
        if (value is null || value.Attempt is null)
        {
            throw Invalid();
        }
        ResourceRetirementAttemptContextValidation.Require(context, limits);
        ResourceRetirementAttemptValidation.Witness(value.Attempt, context, limits);
        if (!ResourceRetirementValidation.Same(value.Context, context)
            || !ResourceRetirementValidation.Same(value.Attempt, ResourceRetirementPreparedOperationStorage.Prepared(attempt))
            || !Enum.IsDefined(value.Stage))
        {
            throw Invalid();
        }
        var action = value.Stage == ResourceRetirementPreparedOperationStage.Persisted
            ? ResourceRetirementPreparedOperationAction.Persist : ResourceRetirementPreparedOperationAction.Close;
        Receipt(value.Receipt, value.ReceiptAuthority, context, attempt, action, limits);
        if (value.Original is null)
        {
            if (value.Stage != ResourceRetirementPreparedOperationStage.Closed || value.Fingerprint is not null
                || value.PersistReceipt is not null || value.PersistAuthority is not null
                || value.Error != ErrorCode.CursorExpired || value.ReceiptAuthority!.EvaluatedAt < attempt.ExpiresAt)
            {
                throw Invalid();
            }
        }
        else
        {
            if (value.Original.Id != attempt.OriginalCommandId || value.Original.Kind != OperationKind.ResourceRetirementPhase
                || value.Original.NativePayload.IsEmpty || value.Original.EvaluatedAt >= attempt.ExpiresAt
                || value.Fingerprint != NativeOperationFingerprint.Compute(value.Original)
                || value.PersistReceipt is null || value.PersistAuthority is null
                || value.Original.EvaluatedAt > value.PersistAuthority.EvaluatedAt
                || value.PersistAuthority.EvaluatedAt >= attempt.ExpiresAt)
            {
                throw Invalid();
            }
            Receipt(value.PersistReceipt, value.PersistAuthority, context, attempt,
                ResourceRetirementPreparedOperationAction.Persist, limits);
        }
        if (value.Stage == ResourceRetirementPreparedOperationStage.Persisted)
        {
            if (value.Original is null || value.Error is not null || value.SafeDetail is not null
                || !ResourceRetirementValidation.Same(value.Receipt, value.PersistReceipt)
                || !ResourceRetirementValidation.Same(value.ReceiptAuthority, value.PersistAuthority))
            {
                throw Invalid();
            }
        }
        else if (value.Error is not { } error || !Enum.IsDefined(error) || string.IsNullOrWhiteSpace(value.SafeDetail)
            || context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage
                && !ResourceRetirementAttemptValidation.RetainedFailure(error, context))
        {
            throw Invalid();
        }
        ResourceRetirementValidation.Bound(value, limits);
    }

    private static void Receipt(ResourceRetirementPhaseReceiptV1 receipt,
        ResourceRetirementPreparedReceiptAuthorityV1? authority, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, ResourceRetirementPreparedOperationAction action, DatabaseLimits limits)
    {
        var request = ResourceRetirementAttemptContextValidation.Request(context);
        if (receipt is null || authority is null || string.IsNullOrWhiteSpace(authority.PrincipalId)
            || authority.PolicyEpoch < ResourceRetirementProtocol.Empty || authority.OperationFingerprint is null
            || authority.OperationFingerprint.Length != DigestCharacters
            || authority.OperationFingerprint.Any(value => value is not (>= DigitFirst and <= DigitLast or >= HexFirst and <= HexLast))
            || receipt.AppliedPosition <= ResourceRetirementProtocol.Empty
            || !ResourceRetirementValidation.Same(receipt, new ResourceRetirementPhaseReceiptV1(
                ResourceRetirementProtocol.Version, request.RetirementId, ResourceRetirementPhase.PreparedOperation,
                ResourceRetirementIdentity.PreparedOperationCommandId(context, attempt.AttemptId, action),
                ResourceRetirementAttemptContextValidation.Lifetime(context),
                ResourceRetirementAttemptContextValidation.ExecutionOwner(context), receipt.AppliedPosition,
                ResourceRetirementAttemptContextValidation.IntentDigest(context))))
        {
            throw Invalid();
        }
        ResourceRetirementValidation.Bound(receipt, limits);
    }

    internal static void Outcome(StoredOutcome? outcome, ResourceRetirementPreparedOperationV1 prepared,
        DatabaseLimits limits)
    {
        var actor = prepared.ReceiptAuthority!;
        if (outcome is null || outcome.Incarnation != prepared.Receipt.PhysicalOwner.Incarnation
            || outcome.PolicyEpoch != actor.PolicyEpoch || outcome.Fingerprint != actor.OperationFingerprint
            || outcome.Partition is not null || outcome.ScopeKind != CommandOutcomeScopeKind.Global
            || outcome.Result is null || outcome.Result.Error is not null || outcome.Result.Json is not null
            || outcome.Result.NativeValue is not ResourceRetirementPhaseResultV1 result
            || result.State is not null || result.Fence is not null || result.OwnerReservation is not null || ResourceRetirementWorkValidation.HasCompletionArm(result)
            || result.PreparationState is not null || !ResourceRetirementValidation.Same(result.Receipt, prepared.Receipt)
            || !ResourceRetirementValidation.Same(result.PreparedOperation, prepared))
        {
            throw Invalid();
        }
        ResourceRetirementValidation.Bound(outcome, limits);
    }

    internal static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
}
