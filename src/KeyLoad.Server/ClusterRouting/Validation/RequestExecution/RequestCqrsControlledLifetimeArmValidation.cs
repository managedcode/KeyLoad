namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsControlledLifetimeArmValidation
{
    internal static bool Valid(RequestCqrsProbeArmRecord arm)
    {
        return arm.Action == RequestCqrsProbeAction.Hold && arm.ReadKind is null && arm.CommandId != Guid.Empty
            && arm.SourceRequestId is null && (arm.SourceArmId is null || arm.SourceArmId != Guid.Empty && arm.SourceArmId != arm.ArmId)
            && (arm.TargetVoter is null || RequestCqrsProbeActivationTargetValidation.Valid(arm.TargetVoter))
            && arm.Partition is { } lifetimePartition && !string.IsNullOrWhiteSpace(lifetimePartition.TenantId)
            && !string.IsNullOrWhiteSpace(lifetimePartition.DatabaseId) && !string.IsNullOrWhiteSpace(lifetimePartition.TransactionDomainId)
            && !string.IsNullOrWhiteSpace(lifetimePartition.PartitionKey);
    }
}
