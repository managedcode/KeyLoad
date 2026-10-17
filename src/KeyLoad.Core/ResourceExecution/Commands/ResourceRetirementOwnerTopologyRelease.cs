using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 ReleaseResourceRetirementOwnerTopology(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementReleaseOwnerTopologyBodyV1>(phase.Body.Span);
        if (body is null || phase.Phase != ResourceRetirementPhase.ReleaseOwnerTopology
            || !PhysicalOwnerEntryValidation.Valid(body.Receiver))
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        ResourceRetirementFinalizationValidation.Require(body.Finalization, Limits);
        var control = body.Finalization.Control;
        var owner = body.Receiver.Owner;
        RequireRetirementPhase(operation, phase, control, owner.PhysicalShardId);
        var view = new ResourceRetirementCleanupReadView(transaction, Limits.MaxQueryReadBytes, Limits.MaxScanRecords);
        RequireRetirementReceiver(view, control, body.Receiver);
        if (ResourceRetirementConservationReleaseStorage.Read(view, control.Lifetime, owner, Limits) is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }
        RequireRetirementOwnerReleaseEvidence(view, body);
        var before = ResourceRetirementConservationRead.ReadFloor(view, Limits.MaxBatchBytes)
            ?? ResourceRetirementConservation.Initial(owner);
        if (!ResourceRetirementConservation.SameOwner(before.PhysicalOwner, owner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
        var count = ResourceRetirementPreparedOperationStorage.ReadReplacementLifeCount(view, control.Lifetime, owner, Limits);
        var receipt = RetirementReceipt(view, control, operation, ResourceRetirementPhase.ReleaseOwnerTopology, position);
        var release = new ResourceRetirementOwnerReleaseV1(body.Finalization, body.Receiver, receipt)
        {
            PreparedReplacementCount = count,
            OutcomeIdentity = new(operation.PrincipalId, principal.PolicyEpoch, CommandFingerprint(operation))
        };
        ResourceRetirementValidation.Bound(release, Limits);
        transaction.PutRecord(ResourceRetirementAuthorityKeys.Release(control.Lifetime, owner), release);
        var added = ResourceRetirementOwnerReleaseAuthority.Capture(view, release, Limits);
        var step = ResourceRetirementConservation.CreateStep(before, control.Lifetime,
            control.Request.RetirementId, operation.Id, position, added);
        var after = ResourceRetirementConservation.Advance(before, step);
        var marker = new ResourceRetirementConservationReleaseV1(release, before, after);
        ResourceRetirementValidation.Bound(step, Limits);
        ResourceRetirementValidation.Bound(after, Limits);
        ResourceRetirementValidation.Bound(marker, Limits);
        transaction.PutRecord(ResourceRetirementConservationKeys.Step(owner, step.Revision), step);
        transaction.PutRecord(ResourceRetirementConservationKeys.Floor(), after);
        transaction.PutRecord(ResourceRetirementConservationKeys.Release(control.Lifetime, owner), marker);
        ResourceRetirementPreparedOperationStorage.ReleaseReplacementBlockers(transaction, control.Lifetime, owner, Limits);
        ResourceRetirementStorage.ReleaseBlocker(transaction, control.Lifetime, Limits);
        var result = new ResourceRetirementPhaseResultV1(receipt, null, null) { OwnerRelease = release };
        ResourceRetirementValidation.Bound(result, Limits);
        return result;
    }

    private void RequireRetirementOwnerReleaseEvidence(IKeyValueView view,
        ResourceRetirementReleaseOwnerTopologyBodyV1 body)
    {
        var completed = body.Finalization;
        var control = completed.Control;
        var owner = body.Receiver.Owner;
        var fence = ResourceRetirementStorage.ReadFence(view, control.Lifetime, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        RequireSameRetirement(fence.Control, control);
        var work = ResourceRetirementWorkStorage.ReadOwner(view, control, Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        var expected = completed.TerminalEvidence.SealedState.WorkBarrierReceipt!.Owners.SingleOrDefault(value =>
            PhysicalOwnerEntryValidation.Same(value.Receiver, body.Receiver));
        var cleanup = ResourceRetirementStorage.Read<ResourceRetirementOwnerCleanupV1>(view,
            ResourceRetirementAuthorityKeys.Owner(ResourceRetirementAuthorityKeys.CleanupSpace, control.Lifetime, owner), Limits);
        var original = completed.TerminalEvidence.Owners.SingleOrDefault(value =>
            PhysicalOwnerEntryValidation.Same(value.Receiver, body.Receiver));
        if (!PhysicalOwnerEntryValidation.SameOwner(fence.Receipt.PhysicalOwner, owner)
            || expected is null || !ResourceRetirementValidation.Same(work, expected)
            || !ResourceRetirementValidation.Same(work.Fence, fence)
            || cleanup is null || original is null || !ResourceRetirementValidation.Same(cleanup, original))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
        if (PhysicalOwnerEntryValidation.SameOwner(control.Catalog.DefaultShard, owner))
        {
            var history = ResourceRetirementFinalizationStorage.ReadHistory(view, control.Lifetime, Limits);
            if (history is null || !ResourceRetirementValidation.Same(history, completed))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
    }
}
