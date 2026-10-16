using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementWorkPhaseValidation
{
    internal static void Require(ResourceRetirementNativeRequest outer, ResourceRetirementPhaseCommandV1 phase,
        DatabaseLimits limits)
    {
        ResourceRetirementControlV1 control;
        switch (phase.Phase)
        {
            case ResourceRetirementPhase.CommitOwnerWorkJoin:
                var commit = NativeSerialization.Deserialize<ResourceRetirementCommitOwnerWorkJoinBodyV1>(phase.Body.Span);
                control = commit.SealedState.Control;
                if (!PhysicalOwnerEntryValidation.Same(commit.Receiver, outer.Destination))
                {
                    throw Invalid();
                }
                break;
            case ResourceRetirementPhase.RecordOwnerWorkJoin:
                control = NativeSerialization.Deserialize<ResourceRetirementRecordOwnerWorkJoinBodyV1>(phase.Body.Span).Control;
                RequireSource(outer);
                break;
            case ResourceRetirementPhase.SealAllWork:
                control = NativeSerialization.Deserialize<ResourceRetirementSealAllWorkBodyV1>(phase.Body.Span).Control;
                RequireSource(outer);
                break;
            default:
                throw Invalid();
        }
        ResourceRetirementNativePhaseValidation.RequireControl(outer, control, limits);
        if (phase.RetirementId != control.Request.RetirementId)
        {
            throw Invalid();
        }
    }

    private static void RequireSource(ResourceRetirementNativeRequest outer)
    {
        if (!PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination))
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
