namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementCleanupPageIdentity
{
    internal static Guid For(ResourceRetirementControlV1 control, RegisteredPhysicalOwnerV1 receiver,
        PartitionRef partition, long ordinal, Guid attemptId)
    {
        if (ordinal <= ResourceRetirementProtocol.Empty || attemptId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        var context = ResourceRetirementCleanupAttemptScope.Context(control, receiver, partition, ordinal);
        return ResourceRetirementCleanupAttemptIdentity.Command(context, ResourceRetirementPhase.CleanupPage, attemptId);
    }
}
