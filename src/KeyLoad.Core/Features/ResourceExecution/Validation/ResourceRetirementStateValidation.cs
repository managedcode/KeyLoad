using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Checks complete durable progress, without treating a caller's Complete flag as authority.</summary>
internal static class ResourceRetirementStateValidation
{
    internal static void State(ResourceRetirementStateV1 state, DatabaseLimits limits)
    {
        if (state is null || state.OwnerReceipts.IsDefault || !Enum.IsDefined(state.Stage))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementValidation.Control(state.Control, limits);
        if (state.OwnerReceipts.Length > state.Control.Directory.Owners.Length
            || state.OwnerReceipts.Length > limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var owners = new HashSet<Guid>();
        foreach (var receipt in state.OwnerReceipts)
        {
            var owner = Member(state.Control, receipt?.PhysicalOwner);
            ResourceRetirementValidation.Receipt(receipt!, state.Control, ResourceRetirementPhase.FenceOwner, owner.Owner, limits);
            if (!owners.Add(owner.Owner.PhysicalShardId))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
        if (state.SealAttempt is not null)
        {
            ResourceRetirementAttemptValidation.Require(state.SealAttempt, state.Control, limits);
        }
        if (state.Stage == ResourceRetirementStage.Retiring)
        {
            if (state.FinalInventory is not null || state.SealedReceipt is not null
                || state.SealAttempt?.Stage == ResourceRetirementAttemptStage.Completed)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
        else
        {
            AllOwners(state);
            if (state.SealAttempt?.Stage != ResourceRetirementAttemptStage.Completed
                || !ResourceRetirementValidation.Same(state.SealAttempt.TerminalReceipt, state.SealedReceipt))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            ResourceRetirementInventoryValidation.Require(state.FinalInventory!, state.Control, limits);
            ResourceRetirementValidation.Receipt(state.SealedReceipt!, state.Control,
                ResourceRetirementPhase.SealBarrier, state.Control.Catalog.DefaultShard, limits, state.SealAttempt.AttemptId);
        }
        ResourceRetirementWorkValidation.State(state, limits);
        ResourceRetirementCompletionValidation.State(state, limits);
        ResourceRetirementValidation.Bound(state, limits);
    }

    internal static void Fence(ResourceRetirementOwnerFenceV1 fence, DatabaseLimits limits)
    {
        if (fence is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementValidation.Control(fence.Control, limits);
        var member = Member(fence.Control, fence.Receipt?.PhysicalOwner);
        ResourceRetirementValidation.Receipt(fence.Receipt!, fence.Control,
            ResourceRetirementPhase.FenceOwner, member.Owner, limits);
        ResourceRetirementValidation.Bound(fence, limits);
    }

    internal static RegisteredPhysicalOwnerV1 Member(ResourceRetirementControlV1 control, PhysicalShardRecord? owner)
        => owner is null ? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed)
            : control.Directory.Owners.SingleOrDefault(member => PhysicalOwnerEntryValidation.SameOwner(member.Owner, owner))
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);

    internal static void AllOwners(ResourceRetirementStateV1 state)
    {
        if (state.OwnerReceipts.Length != state.Control.Directory.Owners.Length
            || state.Control.Directory.Owners.Any(owner => !state.OwnerReceipts.Any(receipt =>
                PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, owner.Owner))))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }
    }
}
