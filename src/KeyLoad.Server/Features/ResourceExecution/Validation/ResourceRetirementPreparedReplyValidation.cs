using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementPreparedReplyValidation
{
    internal static bool Matches(ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt,
        ResourceRetirementPreparedOperationV1 prepared, DatabaseLimits limits)
    {
        ResourceRetirementPreparedOperationValidation.Require(prepared, context, attempt, limits);
        return true;
    }
}
