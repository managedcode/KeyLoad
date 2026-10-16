using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Checks exact immutable cleanup, catalog and owner-release coverage before terminal success.</summary>
internal static class ResourceRetirementCompletionValidation
{
    internal static void State(ResourceRetirementStateV1 state, DatabaseLimits limits)
    {
        if (state.OwnerCleanupReceipts.IsDefault || state.OwnerReleaseReceipts.IsDefault
            || state.OwnerCleanupReceipts.Length > state.Control.Directory.Owners.Length
            || state.OwnerReleaseReceipts.Length > state.Control.Directory.Owners.Length
            || state.OwnerCleanupReceipts.Length > limits.MaxResults
            || state.OwnerReleaseReceipts.Length > limits.MaxResults)
        {
            throw Invalid();
        }
        if (state.Stage == ResourceRetirementStage.Retiring)
        {
            if (!state.OwnerCleanupReceipts.IsEmpty || state.Finalization is not null
                || !state.OwnerReleaseReceipts.IsEmpty || state.CompletionReceipt is not null || state.CompletionOperation is not null
                || state.CompletionPolicyEpoch is not null)
            {
                throw Invalid();
            }

            return;
        }
        var cleanupOwners = new HashSet<Guid>();
        foreach (var cleanup in state.OwnerCleanupReceipts)
        {
            if (state.WorkBarrierReceipt is null)
            {
                throw Invalid();
            }

            ResourceRetirementFinalizationValidation.Owner(cleanup, state, limits);
            if (!cleanupOwners.Add(cleanup.Receiver.Owner.PhysicalShardId))
            {
                throw Invalid();
            }
        }
        if (state.Stage == ResourceRetirementStage.BarrierSealed)
        {
            if (state.Finalization is not null || !state.OwnerReleaseReceipts.IsEmpty
                || state.CompletionReceipt is not null || state.CompletionOperation is not null
                || state.CompletionPolicyEpoch is not null)
            {
                throw Invalid();
            }

            return;
        }
        var finalization = state.Finalization;
        if (finalization is null || cleanupOwners.Count != state.Control.Directory.Owners.Length
            || !ResourceRetirementValidation.Same(finalization.Control, state.Control))
        {
            throw Invalid();
        }

        ResourceRetirementFinalizationValidation.Require(finalization, limits);
        if (finalization.TerminalEvidence.Owners.Any(owner => !state.OwnerCleanupReceipts.Any(actual =>
            ResourceRetirementValidation.Same(actual, owner))))
        {
            throw Invalid();
        }

        var releaseOwners = new HashSet<Guid>();
        foreach (var release in state.OwnerReleaseReceipts)
        {
            Release(release, limits);
            if (!ResourceRetirementValidation.Same(release.Finalization, finalization)
                || !releaseOwners.Add(release.Receiver.Owner.PhysicalShardId))
            {
                throw Invalid();
            }
        }
        if (state.Stage == ResourceRetirementStage.CatalogFinalized)
        {
            if (state.CompletionReceipt is not null || state.CompletionOperation is not null
                || state.CompletionPolicyEpoch is not null)
            {
                throw Invalid();
            }

            return;
        }
        if (state.Stage != ResourceRetirementStage.Completed
            || releaseOwners.Count != state.Control.Directory.Owners.Length)
        {
            throw Invalid();
        }

        ResourceRetirementValidation.Receipt(state.CompletionReceipt!, state.Control,
            ResourceRetirementPhase.Finish, state.Control.Catalog.DefaultShard, limits);
        if (state.CompletionReceipt!.AppliedPosition <= finalization.Receipt.AppliedPosition)
        {
            throw Invalid();
        }

        ResourceRetirementTerminalOperationValidation.Require(state, limits);
    }

    internal static void RequireCompleted(ResourceRetirementStateV1 state, DatabaseLimits limits)
    {
        ResourceRetirementStateValidation.State(state, limits);
        if (state.Stage != ResourceRetirementStage.Completed || state.CompletionReceipt is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }
    }

    internal static void Release(ResourceRetirementOwnerReleaseV1 release, DatabaseLimits limits)
    {
        if (release is null || release.Receiver is null || release.PreparedReplacementCount <= 0)
        {
            throw Invalid();
        }

        ResourceRetirementFinalizationValidation.Require(release.Finalization, limits);
        var control = release.Finalization.Control;
        var owner = ResourceRetirementStateValidation.Member(control, release.Receiver.Owner);
        if (!PhysicalOwnerEntryValidation.Same(owner, release.Receiver))
        {
            throw Invalid();
        }

        ResourceRetirementValidation.Receipt(release.Receipt, control,
            ResourceRetirementPhase.ReleaseOwnerTopology, owner.Owner, limits);
        var cleanup = release.Finalization.TerminalEvidence.Owners.SingleOrDefault(value =>
            PhysicalOwnerEntryValidation.Same(value.Receiver, owner));
        if (cleanup is null || release.Receipt.AppliedPosition <= cleanup.Receipt.AppliedPosition
            || PhysicalOwnerEntryValidation.SameOwner(owner.Owner, control.Catalog.DefaultShard)
                && release.Receipt.AppliedPosition <= release.Finalization.Receipt.AppliedPosition)
        {
            throw Invalid();
        }

        ResourceRetirementOutcomeValidation.Identity(release.OutcomeIdentity, limits);
        ResourceRetirementValidation.Bound(release, limits);
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
}
