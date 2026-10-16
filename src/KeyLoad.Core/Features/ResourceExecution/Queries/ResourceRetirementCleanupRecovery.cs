using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceRetirementCleanupCheckpointV1? ReadResourceRetirementCleanupCheckpoint(string principalId,
        ResourceRetirementControlV1 control, PartitionRef partition, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var receiver = RequireRetirementCleanupRecovery(view, principalId, control);
            if (partition is null)
            {
                throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
            }

            ValidatePartition(partition);
            if (partition.TenantId != control.Lifetime.TenantId || partition.DatabaseId != control.Lifetime.DatabaseId
                || partition.TransactionDomainId != control.Lifetime.TransactionDomainId)
            {
                throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
            }

            var checkpoint = ResourceRetirementCleanupStorage.Read(view, control, receiver, partition, Limits);
            if (checkpoint is not null)
            {
                RequireCleanupCheckpointOriginal(view, checkpoint);
                var saved = ResourceRetirementOutcomeValidation.Read(view, checkpoint.OutcomeIdentity,
                    checkpoint.Receipt, ResourceRetirementPhase.CleanupPage, Limits);
                if (!ResourceRetirementValidation.Same(saved.CleanupCheckpoint, checkpoint))
                {
                    throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
                }
            }
            return checkpoint;
        });
        if (result is not null)
        {
            work.CheckResult(result);
        }

        work.Check();
        return result;
    }

    internal ResourceRetirementOwnerCleanupV1? ReadResourceRetirementOwnerCleanup(string principalId,
        ResourceRetirementControlV1 control, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var receiver = RequireRetirementCleanupRecovery(view, principalId, control);
            var cleanup = ResourceRetirementStorage.Read<ResourceRetirementOwnerCleanupV1>(view,
                ResourceRetirementAuthorityKeys.Owner(ResourceRetirementAuthorityKeys.CleanupSpace,
                    control.Lifetime, receiver.Owner), Limits);
            if (cleanup is null)
            {
                return null;
            }

            if (!ResourceRetirementValidation.Same(cleanup.Control, control)
                || !PhysicalOwnerEntryValidation.Same(cleanup.Receiver, receiver))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }

            ResourceRetirementValidation.Receipt(cleanup.Receipt, control,
                ResourceRetirementPhase.CompleteOwnerCleanup, receiver.Owner, Limits);
            if (cleanup.Partitions.IsDefault || cleanup.Partitions.Length > Limits.MaxResults)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }

            var seen = new HashSet<PartitionRef>();
            foreach (var checkpoint in cleanup.Partitions)
            {
                ResourceRetirementCleanupCheckpointValidation.Require(checkpoint, Limits);
                if (!checkpoint.Complete || !seen.Add(checkpoint.Partition)
                    || !ResourceRetirementValidation.Same(checkpoint.Control, control)
                    || !PhysicalOwnerEntryValidation.Same(checkpoint.Receiver, receiver)
                    || checkpoint.Receipt.AppliedPosition >= cleanup.Receipt.AppliedPosition)
                {
                    throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
                }
            }
            var saved = ResourceRetirementOutcomeValidation.Read(view, cleanup.OutcomeIdentity,
                cleanup.Receipt, ResourceRetirementPhase.CompleteOwnerCleanup, Limits);
            if (!ResourceRetirementValidation.Same(saved.OwnerCleanup, cleanup))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }

            return cleanup;
        });
        if (result is not null)
        {
            work.CheckResult(result);
        }

        work.Check();
        return result;
    }

    internal ResourceRetirementOwnerReleaseV1? ReadResourceRetirementOwnerRelease(string principalId,
        ResourceRetirementControlV1 control, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var receiver = RequireRetirementCleanupRecovery(view, principalId, control);
            var marker = ResourceRetirementConservationReleaseStorage.Read(view, control.Lifetime, receiver.Owner, Limits);
            if (marker is null)
            {
                return null;
            }

            var release = marker.OwnerRelease;
            if (!ResourceRetirementValidation.Same(release.Finalization.Control, control))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }

            var saved = ResourceRetirementOutcomeValidation.Read(view, release.OutcomeIdentity,
                release.Receipt, ResourceRetirementPhase.ReleaseOwnerTopology, Limits);
            if (!ResourceRetirementValidation.Same(saved.OwnerRelease, release))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }

            return release;
        });
        if (result is not null)
        {
            work.CheckResult(result);
        }

        work.Check();
        return result;
    }

    private RegisteredPhysicalOwnerV1 RequireRetirementCleanupRecovery(IKeyValueView view,
        string principalId, ResourceRetirementControlV1 control)
    {
        RequireInventoryAdministrator(view, principalId);
        ResourceRetirementValidation.Control(control, Limits);
        var receiver = ResourceRetirementStateValidation.Member(control, ReadInventoryPhysicalCatalog(view).DefaultShard);
        RequireRetirementReceiver(view, control, receiver);
        var fence = ResourceRetirementStorage.ReadFence(view, control.Lifetime, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        RequireSameRetirement(fence.Control, control);
        var joined = ResourceRetirementWorkStorage.ReadOwner(view, control, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        if (!ResourceRetirementValidation.Same(joined.Fence, fence)
            || !PhysicalOwnerEntryValidation.Same(joined.Receiver, receiver))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        ResourceRetirementWorkValidation.Owner(joined, control, Limits);
        return receiver;
    }
}
