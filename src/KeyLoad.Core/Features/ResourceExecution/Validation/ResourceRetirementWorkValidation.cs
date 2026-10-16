using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Checks bounded durable D4 evidence shape; native transport MUST verify original request/reply MACs before issue.</summary>
internal static class ResourceRetirementWorkValidation
{
    private const int SnapshotDigestBytes = 32;

    internal static void State(ResourceRetirementStateV1 state, DatabaseLimits limits)
    {
        if (state.WorkOwnerReceipts.IsDefault || state.WorkOwnerReceipts.Length > limits.MaxResults
            || state.WorkOwnerReceipts.Length > state.Control.Directory.Owners.Length
            || state.Stage == ResourceRetirementStage.Retiring
                && (!state.WorkOwnerReceipts.IsEmpty || state.WorkBarrierReceipt is not null))
        {
            throw Invalid();
        }
        var seen = new HashSet<Guid>();
        foreach (var owner in state.WorkOwnerReceipts)
        {
            Owner(owner, state.Control, limits);
            if (!seen.Add(owner.Receiver.Owner.PhysicalShardId))
            {
                throw Invalid();
            }
        }
        if (state.WorkBarrierReceipt is not null)
        {
            var barrier = state.WorkBarrierReceipt;
            if (!ResourceRetirementValidation.Same(barrier.Control, state.Control)
                || !ResourceRetirementValidation.Same(barrier.SealedReceipt, state.SealedReceipt)
                || barrier.Owners.IsDefault || barrier.Owners.Length != state.Control.Directory.Owners.Length
                || state.WorkOwnerReceipts.Length != state.Control.Directory.Owners.Length)
            {
                throw Invalid();
            }
            var barrierOwners = new HashSet<Guid>();
            foreach (var owner in barrier.Owners)
            {
                Owner(owner, state.Control, limits);
                if (!barrierOwners.Add(owner.Receiver.Owner.PhysicalShardId)
                    || !state.WorkOwnerReceipts.Any(original => ResourceRetirementValidation.Same(original, owner)))
                {
                    throw Invalid();
                }
            }
            ResourceRetirementValidation.Receipt(barrier.Receipt, state.Control,
                ResourceRetirementPhase.SealAllWork, state.Control.Catalog.DefaultShard, limits);
            ResourceRetirementValidation.Bound(barrier, limits);
        }
    }

    internal static void RequireBarrier(ResourceRetirementStateV1 state, DatabaseLimits limits)
    {
        ResourceRetirementStateValidation.State(state, limits);
        if (state.Stage != ResourceRetirementStage.BarrierSealed || state.WorkBarrierReceipt is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }
    }

    internal static void Owner(ResourceRetirementWorkOwnerReceiptV1 owner,
        ResourceRetirementControlV1 control, DatabaseLimits limits)
    {
        if (owner is null || owner.Receiver is null || owner.Fence is null
            || !ResourceRetirementValidation.Same(owner.Control, control)
            || !ResourceRetirementValidation.Same(owner.Fence.Control, control)
            || owner.Evidence.IsDefaultOrEmpty || owner.Evidence.Length > limits.MaxResults)
        {
            throw Invalid();
        }
        ResourceRetirementStateValidation.Fence(owner.Fence, limits);
        var member = ResourceRetirementStateValidation.Member(control, owner.Receiver.Owner);
        if (!PhysicalOwnerEntryValidation.Same(member, owner.Receiver)
            || !PhysicalOwnerEntryValidation.SameOwner(owner.Fence.Receipt.PhysicalOwner, member.Owner)
            || owner.Evidence.Length != member.Owner.VoterIds.Length)
        {
            throw Invalid();
        }
        ResourceRetirementValidation.Receipt(owner.Receipt, control,
            ResourceRetirementPhase.CommitOwnerWorkJoin, member.Owner, limits);
        var voters = new HashSet<string>(StringComparer.Ordinal);
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new HashSet<Guid>();
        foreach (var item in owner.Evidence)
        {
            Evidence(item, owner.Fence, member.Owner, limits);
            if (!voters.Add(item.Witness.VoterId) || !addresses.Add(item.Witness.FullSiloAddress)
                || !nodes.Add(item.Witness.StoreNodeId))
            {
                throw Invalid();
            }
        }
        if (!voters.SetEquals(member.Owner.VoterIds))
        {
            throw Invalid();
        }

        ResourceRetirementValidation.Bound(owner, limits);
    }

    internal static void Evidence(ResourceRetirementWorkJoinEvidenceV1 item,
        ResourceRetirementOwnerFenceV1 fence, PhysicalShardRecord actualOwner, DatabaseLimits limits)
    {
        if (item is null || item.Witness is null || item.SignedNativeRequest.IsEmpty || item.SignedNativeReply.IsEmpty)
        {
            throw Invalid();
        }
        var witness = item.Witness;
        if (witness.ActualOwner is null || witness.Version != ResourceRetirementProtocol.Version
            || !ResourceRetirementValidation.Same(witness.Fence, fence)
            || !PhysicalOwnerEntryValidation.SameOwner(witness.ActualOwner, actualOwner)
            || string.IsNullOrWhiteSpace(witness.VoterId)
            || !actualOwner.VoterIds.Contains(witness.VoterId, StringComparer.Ordinal)
            || string.IsNullOrWhiteSpace(witness.FullSiloAddress)
            || witness.FullSiloAddress != witness.FullSiloAddress.Trim()
            || witness.StoreNodeId == Guid.Empty || witness.ReadGeneration < 0
            || witness.MatchedCount < 0
            || witness.MaximumPublicationGeneration < 0 || witness.SnapshotSha256.Length != SnapshotDigestBytes
            || witness.MatchedCount == 0 && witness.MaximumPublicationGeneration != 0)
        {
            throw Invalid();
        }
        ResourceRetirementWorkOriginValidation.RequireWitness(fence.Control, witness, limits);
        ResourceRetirementValidation.Bound(item, limits);
    }

    internal static bool HasCompletionArm(ResourceRetirementPhaseResultV1 result)
        => result.WorkOwnerReceipt is not null || result.WorkBarrierReceipt is not null
            || result.CleanupCheckpoint is not null || result.OwnerCleanup is not null || result.Finalization is not null
            || result.OwnerRelease is not null || result.DropParent is not null || result.CleanupAttempt is not null;

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
}
