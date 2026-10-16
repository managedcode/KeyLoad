using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementNativeAttemptValidation
{
    internal static bool SameIdentity(ResourceRetirementAttemptV1 left, ResourceRetirementAttemptV1 right)
        => left.AttemptId == right.AttemptId && left.Ordinal == right.Ordinal && left.ExpiresAt == right.ExpiresAt
            && left.PrincipalId == right.PrincipalId && left.PolicyEpoch == right.PolicyEpoch
            && left.OriginalCommandId == right.OriginalCommandId
            && ResourceRetirementValidation.Same(left.PrepareReceipt, right.PrepareReceipt);
}
