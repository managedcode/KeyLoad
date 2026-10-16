using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementCompletionPhase
{
    internal static bool Handles(ResourceRetirementPhase phase) => phase is ResourceRetirementPhase.CleanupPage
        or ResourceRetirementPhase.RecordOwnerCleanup or ResourceRetirementPhase.FinalizeCatalog
        or ResourceRetirementPhase.ReleaseOwnerTopology or ResourceRetirementPhase.RecordOwnerRelease
        or ResourceRetirementPhase.Finish or ResourceRetirementPhase.CompleteOwnerCleanup
        or ResourceRetirementPhase.PersistDropParent or ResourceRetirementPhase.CompleteDropParent;

    internal static void Require(ResourceRetirementNativeRequest outer, ResourceRetirementPhaseCommandV1 phase,
        DatabaseLimits limits)
    {
        ResourceRetirementControlV1? control = null;
        RegisteredPhysicalOwnerV1? receiver = null;
        switch (phase.Phase)
        {
            case ResourceRetirementPhase.CleanupPage:
                var page = Read<ResourceRetirementCleanupPageBodyV1>(phase);
                control = page.SealedState.Control;
                receiver = page.Receiver;
                break;
            case ResourceRetirementPhase.CompleteOwnerCleanup:
                var complete = Read<ResourceRetirementCompleteOwnerCleanupBodyV1>(phase);
                control = complete.SealedState.Control;
                receiver = complete.Receiver;
                break;
            case ResourceRetirementPhase.RecordOwnerCleanup:
                control = Read<ResourceRetirementRecordOwnerCleanupBodyV1>(phase).Control;
                break;
            case ResourceRetirementPhase.FinalizeCatalog:
                control = Read<ResourceRetirementFinalizeCatalogBodyV1>(phase).Control;
                break;
            case ResourceRetirementPhase.ReleaseOwnerTopology:
                var release = Read<ResourceRetirementReleaseOwnerTopologyBodyV1>(phase);
                control = release.Finalization.Control;
                receiver = release.Receiver;
                break;
            case ResourceRetirementPhase.RecordOwnerRelease:
                control = Read<ResourceRetirementRecordOwnerReleaseBodyV1>(phase).Control;
                break;
            case ResourceRetirementPhase.Finish:
                control = Read<ResourceRetirementFinishBodyV1>(phase).Control;
                break;
            case ResourceRetirementPhase.PersistDropParent:
            case ResourceRetirementPhase.CompleteDropParent:
                break;
            default:
                throw Invalid();
        }
        if (control is not null)
        {
            ResourceRetirementNativePhaseValidation.RequireControl(outer, control, limits);
        }
        if (receiver is not null ? !PhysicalOwnerEntryValidation.Same(receiver, outer.Destination)
            : !PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination))
        {
            throw Invalid();
        }
    }

    private static T Read<T>(ResourceRetirementPhaseCommandV1 phase)
        => NativeSerialization.Deserialize<T>(phase.Body.Span);

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
