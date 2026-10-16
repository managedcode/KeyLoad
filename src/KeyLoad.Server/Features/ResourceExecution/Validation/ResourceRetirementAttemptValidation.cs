using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementAttemptValidation
{
    private const int MinimumOrdinal = 0;
    private const long MinimumPolicyEpoch = 0;

    internal static void Require(ResourceRetirementAttemptV1 attempt, ResourceRetirementControlV1 control,
        PhysicalShardRecord source)
    {
        if (attempt.AttemptId == Guid.Empty || attempt.Ordinal <= MinimumOrdinal || !Enum.IsDefined(attempt.Stage)
            || string.IsNullOrEmpty(attempt.PrincipalId) || attempt.PolicyEpoch < MinimumPolicyEpoch
            || attempt.ExpiresAt == default || attempt.OriginalCommandId != ResourceRetirementIdentity.AttemptCommandId(
                control.Request, ResourceRetirementPhase.SealBarrier, attempt.AttemptId, source.PhysicalShardId))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        ResourceRetirementReceiptValidation.RequireReceipt(attempt.PrepareReceipt, control,
            ResourceRetirementPhase.PrepareAttempt, source, source.PhysicalShardId, attempt.AttemptId);
        if (attempt.Stage == ResourceRetirementAttemptStage.Prepared
            && (attempt.OriginalFingerprint is not null || attempt.TerminalReceipt is not null
                || attempt.Error is not null || attempt.SafeDetail is not null))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        if (attempt.Stage == ResourceRetirementAttemptStage.Completed)
        {
            if (attempt.TerminalReceipt is not { } receipt || string.IsNullOrEmpty(attempt.OriginalFingerprint)
                || attempt.Error is not null || attempt.SafeDetail is not null)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
            ResourceRetirementReceiptValidation.RequireReceipt(receipt, control,
                ResourceRetirementPhase.SealBarrier, source, source.PhysicalShardId, attempt.AttemptId);
        }
    }

    internal static bool SameAdmission(ResourceRetirementAttemptV1 left, ResourceRetirementAttemptV1 right)
        => left.AttemptId == right.AttemptId && left.Ordinal == right.Ordinal && left.ExpiresAt == right.ExpiresAt
            && left.PrincipalId == right.PrincipalId && left.PolicyEpoch == right.PolicyEpoch
            && left.OriginalCommandId == right.OriginalCommandId
            && ResourceRetirementReceiptValidation.Same(left.PrepareReceipt, right.PrepareReceipt);
}
