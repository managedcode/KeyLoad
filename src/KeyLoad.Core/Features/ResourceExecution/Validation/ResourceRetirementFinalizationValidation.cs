using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Validates bounded typed terminal evidence; authenticated receiver transport owns its provenance.</summary>
internal static class ResourceRetirementFinalizationValidation
{
    internal static void Require(ResourceRetirementFinalizationV1 completed, DatabaseLimits limits)
    {
        try
        {
            RequireCore(completed, limits);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Validation or ErrorCode.TokenInvalidated
            or ErrorCode.OwnershipLost or ErrorCode.RecoveryRequired or ErrorCode.BudgetExceeded)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    private static void RequireCore(ResourceRetirementFinalizationV1 completed, DatabaseLimits limits)
    {
        if (completed is null || completed.TerminalEvidence is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementValidation.Control(completed.Control, limits);
        var evidence = completed.TerminalEvidence;
        var state = evidence.SealedState;
        if (state is null || state.Stage != ResourceRetirementStage.BarrierSealed
            || !ResourceRetirementValidation.Same(state.Control, completed.Control)
            || state.OwnerCleanupReceipts.IsDefault || state.OwnerReleaseReceipts.IsDefault
            || !state.OwnerCleanupReceipts.IsEmpty || state.Finalization is not null
            || !state.OwnerReleaseReceipts.IsEmpty || state.CompletionReceipt is not null
            || state.CompletionOperation is not null || state.CompletionPolicyEpoch is not null
            || evidence.Owners.IsDefault || evidence.Owners.Length != completed.Control.Directory.Owners.Length
            || evidence.Owners.Length > limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementWorkValidation.RequireBarrier(state, limits);
        var seen = new HashSet<Guid>();
        foreach (var owner in evidence.Owners)
        {
            Owner(owner, state, limits);
            if (!seen.Add(owner.Receiver.Owner.PhysicalShardId))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
        ResourceRetirementValidation.Receipt(completed.Receipt, completed.Control,
            ResourceRetirementPhase.FinalizeCatalog, completed.Control.Catalog.DefaultShard, limits);
        var sourceCleanup = evidence.Owners.Single(owner => PhysicalOwnerEntryValidation.SameOwner(
            owner.Receiver.Owner, completed.Control.Catalog.DefaultShard));
        if (completed.Receipt.AppliedPosition <= sourceCleanup.Receipt.AppliedPosition)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        ResourceRetirementFinalizationReceiptValidation.Require(completed, limits);
        ResourceRetirementValidation.Bound(completed, limits);
    }

    internal static void Owner(ResourceRetirementOwnerCleanupV1 owner,
        ResourceRetirementStateV1 sealedState, DatabaseLimits limits)
    {
        if (owner is null || owner.Receiver is null || owner.Partitions.IsDefault
            || owner.Partitions.Length > limits.MaxResults
            || !ResourceRetirementValidation.Same(owner.Control, sealedState.Control))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var member = ResourceRetirementStateValidation.Member(sealedState.Control, owner.Receiver.Owner);
        if (!PhysicalOwnerEntryValidation.Same(member, owner.Receiver))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementValidation.Receipt(owner.Receipt, owner.Control,
            ResourceRetirementPhase.CompleteOwnerCleanup, member.Owner, limits);
        var work = sealedState.WorkBarrierReceipt!.Owners.SingleOrDefault(value =>
            PhysicalOwnerEntryValidation.Same(value.Receiver, member));
        if (work is null || owner.Receipt.AppliedPosition <= work.Receipt.AppliedPosition
            || PhysicalOwnerEntryValidation.SameOwner(member.Owner, owner.Control.Catalog.DefaultShard)
                && owner.Receipt.AppliedPosition <= sealedState.WorkBarrierReceipt.Receipt.AppliedPosition)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        var expected = sealedState.FinalInventory!.Entries
            .Where(entry => PhysicalOwnerEntryValidation.SameOwner(entry.ObservedOwner, member.Owner))
            .Select(entry => entry.Partition).ToHashSet();
        var seen = new HashSet<PartitionRef>();
        foreach (var checkpoint in owner.Partitions)
        {
            if (checkpoint is null || checkpoint.Partition is null || checkpoint.Ordinal <= 0
                || !checkpoint.Complete || checkpoint.FamilyOrdinal != ResourceRetirementTableCleanupFamilies.Count
                || !checkpoint.AfterKey.IsEmpty || !seen.Add(checkpoint.Partition)
                || !expected.Contains(checkpoint.Partition)
                || !ResourceRetirementValidation.Same(checkpoint.Control, owner.Control)
                || !PhysicalOwnerEntryValidation.Same(checkpoint.Receiver, member))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            ResourceRetirementCleanupCheckpointValidation.Require(checkpoint, limits);
            var receipt = checkpoint.Receipt;
            if (receipt is null || receipt.Version != ResourceRetirementProtocol.Version
                || receipt.RetirementId != owner.Control.Request.RetirementId
                || receipt.Phase != ResourceRetirementPhase.CleanupPage || receipt.CommandId == Guid.Empty
                || receipt.AppliedPosition <= 0 || receipt.AppliedPosition >= owner.Receipt.AppliedPosition
                || receipt.IntentDigest != owner.Control.IntentDigest
                || !ResourceRetirementValidation.Same(receipt.Lifetime, owner.Control.Lifetime)
                || !PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, member.Owner))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
        if (!expected.SetEquals(seen))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementOutcomeValidation.Identity(owner.OutcomeIdentity, limits);
        ResourceRetirementValidation.Bound(owner, limits);
    }
}
