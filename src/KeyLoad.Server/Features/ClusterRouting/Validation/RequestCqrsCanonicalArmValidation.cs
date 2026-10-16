namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsCanonicalArmValidation
{
    internal static bool Valid(RequestCqrsProbeArmRecord arm)
        => arm.Phase == RequestCqrsProbePhase.ResourceOwnerInventoryOwnerValidation
            ? ValidResourceOwnerInventory(arm)
            : ValidCanonical(arm);

    private static bool ValidResourceOwnerInventory(RequestCqrsProbeArmRecord arm)
        => arm.ReadKind == KeyLoad.Clustering.GrainReadKind.ResourceOwnerInventory
            && arm.Action == RequestCqrsProbeAction.Hold && arm.CommandId == Guid.Empty
            && arm.Partition is null && arm.SourceRequestId is null && arm.SourceArmId is null
            && (arm.TargetVoter is null || RequestCqrsProbeActivationTargetValidation.Valid(arm.TargetVoter));

    private static bool ValidCanonical(RequestCqrsProbeArmRecord arm)
    {
        if (arm.Phase == RequestCqrsProbePhase.ControlledLifetimeReplayBeforeDispatch)
        {
            return RequestCqrsControlledLifetimeArmValidation.Valid(arm);
        }
        if (arm.Phase == RequestCqrsProbePhase.ReplicaEntryAcknowledged)
        {
            return arm.Action == RequestCqrsProbeAction.ThrowOrdinary && arm.ReadKind is null
                && arm.CommandId != Guid.Empty && arm.SourceRequestId is { } logRequest && logRequest != Guid.Empty
                && arm.SourceArmId is { } logSource && logSource != Guid.Empty && logSource != arm.ArmId
                && arm.TargetVoter is { } logTarget && RequestCqrsProbeActivationTargetValidation.Valid(logTarget)
                && arm.Partition is { } logPartition && !string.IsNullOrWhiteSpace(logPartition.TenantId)
                && !string.IsNullOrWhiteSpace(logPartition.DatabaseId)
                && !string.IsNullOrWhiteSpace(logPartition.TransactionDomainId) && !string.IsNullOrWhiteSpace(logPartition.PartitionKey);
        }
        if (arm.Phase is RequestCqrsProbePhase.EventVectorFirstCapturesReady
            or RequestCqrsProbePhase.EventVectorFirstAdmitPrepared)
        {
            return RequestCqrsEventVectorProbePair.ValidArm(arm);
        }
        if (arm.Phase == RequestCqrsProbePhase.ReplicaSnapshotReadiness)
        {
            return arm.ReadKind == KeyLoad.Clustering.GrainReadKind.NodeStatus
                && arm.Action == RequestCqrsProbeAction.Hold && arm.CommandId == Guid.Empty
                && arm.Partition is null && arm.SourceRequestId is null && arm.SourceArmId is null
                && arm.TargetVoter is { } target && RequestCqrsProbeActivationTargetValidation.Valid(target);
        }
        if (arm.Phase == RequestCqrsProbePhase.AuthorizationReload && arm.SourceArmId is not null)
        {
            return RequestCqrsSampleChunkProbePair.ValidAdjunct(arm);
        }
        if (arm.Phase == RequestCqrsProbePhase.ParentReceiverIssueObserved)
        {
            return arm.Action == RequestCqrsProbeAction.Hold && arm.ReadKind is null && arm.CommandId != Guid.Empty
                && arm.SourceRequestId is { } sourceRequest && sourceRequest != Guid.Empty
                && arm.SourceArmId is { } sourceArm && sourceArm != Guid.Empty && sourceArm != arm.ArmId
                && arm.Partition is null && arm.TargetVoter is null;
        }
        if (arm.Phase == RequestCqrsProbePhase.SubmitReturned && arm.Partition is { } movementPartition)
        {
            return arm.Action == RequestCqrsProbeAction.Hold && arm.ReadKind is null && arm.CommandId != Guid.Empty
                && arm.TargetVoter is null && arm.SourceRequestId is null && arm.SourceArmId is null
                && !string.IsNullOrWhiteSpace(movementPartition.TenantId)
                && !string.IsNullOrWhiteSpace(movementPartition.DatabaseId)
                && !string.IsNullOrWhiteSpace(movementPartition.TransactionDomainId)
                && !string.IsNullOrWhiteSpace(movementPartition.PartitionKey);
        }
        if (arm.Phase != RequestCqrsProbePhase.CanonicalJournalFlushed)
        {
            return arm.Partition is null && arm.SourceRequestId is null && arm.SourceArmId is null
            && (arm.TargetVoter is null || arm.Phase == RequestCqrsProbePhase.BeforeSubmit
                && arm.Action == RequestCqrsProbeAction.Hold && arm.ReadKind is null && arm.CommandId != Guid.Empty
                && RequestCqrsProbeActivationTargetValidation.Valid(arm.TargetVoter));
        }
        return arm.Action == RequestCqrsProbeAction.Hold && arm.ReadKind is null
            && arm.CommandId != Guid.Empty && arm.SourceRequestId is { } request && request != Guid.Empty
            && arm.SourceArmId is { } source && source != Guid.Empty && source != arm.ArmId
            && !string.IsNullOrWhiteSpace(arm.TargetVoter) && arm.Partition is { } partition
            && !string.IsNullOrWhiteSpace(partition.TenantId) && !string.IsNullOrWhiteSpace(partition.DatabaseId)
            && !string.IsNullOrWhiteSpace(partition.TransactionDomainId) && !string.IsNullOrWhiteSpace(partition.PartitionKey);
    }
}
