using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 CompleteResourceRetirementOwnerCleanup(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementCompleteOwnerCleanupBodyV1>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        if (!PhysicalOwnerEntryValidation.Valid(body.Receiver))
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }

        var immutable = body.SealedState;
        if (immutable is null || immutable.OwnerCleanupReceipts.IsDefault || !immutable.OwnerCleanupReceipts.IsEmpty
            || immutable.Finalization is not null || immutable.OwnerReleaseReceipts.IsDefault
            || !immutable.OwnerReleaseReceipts.IsEmpty || immutable.CompletionReceipt is not null
            || immutable.CompletionOperation is not null || immutable.CompletionPolicyEpoch is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        ResourceRetirementWorkValidation.RequireBarrier(immutable, Limits);
        var control = body.SealedState.Control;
        RequireRetirementPhase(operation, phase, control, body.Receiver.Owner.PhysicalShardId);
        var view = new ResourceRetirementCleanupReadView(transaction, Limits.MaxQueryReadBytes, Limits.MaxScanRecords);
        RequireRetirementReceiver(view, control, body.Receiver);
        var fence = ResourceRetirementStorage.ReadFence(view, control.Lifetime, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        RequireSameRetirement(fence.Control, control);
        var work = ResourceRetirementWorkStorage.ReadOwner(view, control, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        var original = body.SealedState.WorkBarrierReceipt!.Owners.SingleOrDefault(value =>
            PhysicalOwnerEntryValidation.Same(value.Receiver, body.Receiver));
        if (original is null || !ResourceRetirementValidation.Same(work, original)
            || !ResourceRetirementValidation.Same(work.Fence, fence))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }

        var key = ResourceRetirementAuthorityKeys.Owner(ResourceRetirementAuthorityKeys.CleanupSpace,
            control.Lifetime, body.Receiver.Owner);
        if (view.ReadOwnedValue(key) is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }

        var checkpoints = ImmutableArray.CreateBuilder<ResourceRetirementCleanupCheckpointV1>();
        foreach (var entry in body.SealedState.FinalInventory!.Entries.Where(entry =>
            PhysicalOwnerEntryValidation.SameOwner(entry.ObservedOwner, body.Receiver.Owner)))
        {
            checkpoints.Add(ResourceRetirementCleanupStorage.Read(view, control, body.Receiver, entry.Partition, Limits)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete));
        }
        var receipt = RetirementReceipt(view, control, operation, phase.Phase, position);
        var cleanup = new ResourceRetirementOwnerCleanupV1(control, body.Receiver, checkpoints.ToImmutable(), receipt)
        {
            OutcomeIdentity = new(operation.PrincipalId, principal.PolicyEpoch, CommandFingerprint(operation))
        };
        ResourceRetirementFinalizationValidation.Owner(cleanup, body.SealedState, Limits);
        transaction.PutRecord(key, cleanup);
        return new(receipt, null, null)
        {
            OwnerCleanup = cleanup
        };
    }
}
