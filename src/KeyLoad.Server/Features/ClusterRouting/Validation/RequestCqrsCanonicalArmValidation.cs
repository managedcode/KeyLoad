namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsCanonicalArmValidation
{
    internal static bool Valid(RequestCqrsProbeArmRecord arm)
    {
        if (arm.Phase != RequestCqrsProbePhase.CanonicalJournalFlushed)
        { return arm.Partition is null && arm.SourceRequestId is null && arm.TargetVoter is null && arm.SourceArmId is null; }
        return arm.Action == RequestCqrsProbeAction.Hold && arm.ReadKind is null
            && arm.CommandId != Guid.Empty && arm.SourceRequestId is { } request && request != Guid.Empty
            && arm.SourceArmId is { } source && source != Guid.Empty && source != arm.ArmId
            && !string.IsNullOrWhiteSpace(arm.TargetVoter) && arm.Partition is { } partition
            && !string.IsNullOrWhiteSpace(partition.TenantId) && !string.IsNullOrWhiteSpace(partition.DatabaseId)
            && !string.IsNullOrWhiteSpace(partition.TransactionDomainId) && !string.IsNullOrWhiteSpace(partition.PartitionKey);
    }
}
