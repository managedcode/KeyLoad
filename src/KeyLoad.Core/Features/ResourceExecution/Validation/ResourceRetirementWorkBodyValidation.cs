using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementWorkBodyValidation
{
    internal static Guid Require(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        ResourceRetirementValidation.Bound(phase, limits);
        return phase.Phase switch
        {
            ResourceRetirementPhase.CommitOwnerWorkJoin => Commit(phase, limits),
            ResourceRetirementPhase.RecordOwnerWorkJoin => Record(phase, limits),
            ResourceRetirementPhase.SealAllWork => Seal(phase, limits),
            _ => throw Invalid()
        };
    }

    private static Guid Commit(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementCommitOwnerWorkJoinBodyV1>(phase.Body.Span);
        ResourceRetirementStateValidation.State(body.SealedState, limits);
        var control = body.SealedState.Control;
        Parent(phase, control);
        var member = ResourceRetirementStateValidation.Member(control, body.Receiver.Owner);
        if (body.SealedState.Stage != ResourceRetirementStage.BarrierSealed
            || !PhysicalOwnerEntryValidation.Same(member, body.Receiver)
            || body.Evidence.IsDefaultOrEmpty || body.Evidence.Length != member.Owner.VoterIds.Length)
        {
            throw Invalid();
        }
        foreach (var evidence in body.Evidence)
        {
            ResourceRetirementWorkValidation.Evidence(evidence, evidence.Witness.Fence, member.Owner, limits);
            if (!ResourceRetirementValidation.Same(evidence.Witness.Fence.Control, control))
            {
                throw Invalid();
            }
        }
        return ResourceRetirementIdentity.PhaseCommandId(control.Request, phase.Phase, member.Owner.PhysicalShardId);
    }

    private static Guid Record(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementRecordOwnerWorkJoinBodyV1>(phase.Body.Span);
        Parent(phase, body.Control);
        ResourceRetirementWorkValidation.Owner(body.OwnerReceipt, body.Control, limits);
        if (body.DurableReads.IsDefaultOrEmpty || body.DurableReads.Length != body.OwnerReceipt.Receiver.Owner.VoterIds.Length)
        {
            throw Invalid();
        }
        ResourceRetirementValidation.Bound(body, limits);
        return ResourceRetirementIdentity.PhaseCommandId(body.Control.Request, phase.Phase,
            body.OwnerReceipt.Receiver.Owner.PhysicalShardId);
    }

    private static Guid Seal(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementSealAllWorkBodyV1>(phase.Body.Span);
        ResourceRetirementValidation.Control(body.Control, limits);
        Parent(phase, body.Control);
        return ResourceRetirementIdentity.PhaseCommandId(body.Control.Request, phase.Phase,
            body.Control.Catalog.DefaultShard.PhysicalShardId);
    }

    private static void Parent(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementControlV1 control)
    {
        if (phase.RetirementId != control.Request.RetirementId)
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
}
