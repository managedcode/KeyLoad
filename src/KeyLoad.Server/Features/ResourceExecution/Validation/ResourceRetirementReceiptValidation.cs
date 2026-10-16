using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementReceiptValidation
{
    private const long NoAppliedPosition = 0;

    internal static void RequireControl(ResourceRetirementControlV1 control, ResourceRetirementRequestV1 request,
        PhysicalShardRecord source)
    {
        PhysicalOwnerDirectoryValidation.Validate(control.Directory);
        if (control.Request != request || control.Lifetime.TenantId != request.TenantId
            || control.Lifetime.DatabaseId != request.DatabaseId || control.Lifetime.ResourceName != request.ResourceName
            || control.Lifetime.Incarnation != request.ExpectedIncarnation || string.IsNullOrEmpty(control.IntentDigest)
            || control.Catalog.DefaultShard is not { } catalogOwner
            || !PhysicalOwnerEntryValidation.SameOwner(catalogOwner, source)
            || !PhysicalOwnerEntryValidation.SameOwner(control.Directory.ControlOwner, source))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementNativeProtocol.Unavailable);
        }
        RequireReceipt(control.BeginReceipt, control, ResourceRetirementPhase.Begin, source, source.PhysicalShardId);
    }

    internal static void RequireReceipt(ResourceRetirementPhaseReceiptV1 receipt, ResourceRetirementControlV1 control,
        ResourceRetirementPhase phase, PhysicalShardRecord owner, Guid identityOwner, Guid attemptId = default)
    {
        var commandId = phase is ResourceRetirementPhase.PrepareAttempt or ResourceRetirementPhase.SealBarrier or ResourceRetirementPhase.PreparedOperation
            ? ResourceRetirementIdentity.AttemptCommandId(control.Request, phase, attemptId, identityOwner)
            : ResourceRetirementIdentity.PhaseCommandId(control.Request, phase, identityOwner);
        if (receipt.Version != ResourceRetirementNativeProtocol.Version || receipt.RetirementId != control.Request.RetirementId
            || receipt.Phase != phase || receipt.CommandId != commandId
            || receipt.Lifetime != control.Lifetime || receipt.AppliedPosition <= NoAppliedPosition || receipt.IntentDigest != control.IntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, owner))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
    }

    internal static void RequireState(ResourceRetirementStateV1 state, ResourceRetirementRequestV1 request,
        RegisteredPhysicalOwnerV1 source)
    {
        RequireControl(state.Control, request, source.Owner);
        if (state.OwnerReceipts.IsDefault || state.OwnerReceipts.Length > state.Control.Directory.Owners.Length
            || !Enum.IsDefined(state.Stage))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        var owners = new HashSet<Guid>();
        foreach (var receipt in state.OwnerReceipts)
        {
            var owner = state.Control.Directory.Owners.FirstOrDefault(value =>
                PhysicalOwnerEntryValidation.SameOwner(value.Owner, receipt.PhysicalOwner));
            if (owner is null || !owners.Add(owner.Owner.PhysicalShardId))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
            RequireReceipt(receipt, state.Control, ResourceRetirementPhase.FenceOwner,
                owner.Owner, owner.Owner.PhysicalShardId);
        }
        if (state.SealAttempt is { } attempt)
        {
            ResourceRetirementAttemptValidation.Require(attempt, state.Control, source.Owner);
        }
        if (state.Stage == ResourceRetirementStage.BarrierSealed)
        {
            if (state.SealedReceipt is not { } sealedReceipt || state.FinalInventory is not { Complete: true } inventory
                || inventory.CoverageKind != ResourceOwnerInventoryCoverageKind.ConservativeMayContain
                || state.OwnerReceipts.Length != state.Control.Directory.Owners.Length
                || state.SealAttempt is not { Stage: ResourceRetirementAttemptStage.Completed } sealedAttempt)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
            ResourceRetirementInventoryValidation.Require(state.Control, sealedAttempt, inventory);
            if (!Same(sealedReceipt, sealedAttempt.TerminalReceipt))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
            RequireReceipt(sealedReceipt, state.Control, ResourceRetirementPhase.SealBarrier,
                source.Owner, source.Owner.PhysicalShardId, sealedAttempt.AttemptId);
        }
        else if (state.SealedReceipt is not null || state.FinalInventory is not null
            || state.SealAttempt is { Stage: ResourceRetirementAttemptStage.Completed })
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
    }

    internal static void RequireFence(ResourceRetirementOwnerFenceV1 fence, ResourceRetirementControlV1 control,
        RegisteredPhysicalOwnerV1 owner)
    {
        if (!Same(fence.Control, control))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        RequireReceipt(fence.Receipt, control, ResourceRetirementPhase.FenceOwner,
            owner.Owner, owner.Owner.PhysicalShardId);
    }

    internal static bool Same<T>(T left, T right)
        => NativeSerialization.Serialize(left).AsSpan().SequenceEqual(NativeSerialization.Serialize(right));
    internal static ResourceRetirementStateV1 RequireSourcePhase(ResourceRetirementPhaseResultV1 result,
        ResourceRetirementControlV1 control, ResourceRetirementPhase phase, RegisteredPhysicalOwnerV1 source,
        Guid identityOwner, Guid attemptId = default)
    {
        var state = result.State ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementReceiptValidation.RequireState(state, control.Request, source);
        ResourceRetirementReceiptValidation.RequireReceipt(result.Receipt, control, phase, source.Owner, identityOwner, attemptId);
        if (result.Fence is not null || result.PreparedOperation is not null || !ResourceRetirementReceiptValidation.Same(state.Control, control))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        return state;
    }
}
