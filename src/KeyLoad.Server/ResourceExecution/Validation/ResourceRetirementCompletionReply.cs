using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementCompletionReply
{
    internal static void Require(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result,
        DatabaseLimits limits)
    {
        if (result.Fence is not null || result.PreparedOperation is not null || result.PreparationState is not null
            || result.OwnerReservation is not null || result.WorkOwnerReceipt is not null || result.WorkBarrierReceipt is not null)
        {
            throw Invalid();
        }
        var allowed = result with
        {
            State = null,
            CleanupCheckpoint = null,
            OwnerCleanup = null,
            Finalization = null,
            OwnerRelease = null,
            DropParent = null
        };
        var expected = new ResourceRetirementPhaseResultV1(result.Receipt, null, null);
        if (!ResourceRetirementValidation.Same(allowed, expected))
        {
            throw Invalid();
        }
        switch (phase.Phase)
        {
            case ResourceRetirementPhase.CleanupPage:
                Page(phase, result, limits);
                break;
            case ResourceRetirementPhase.CompleteOwnerCleanup:
                OwnerCleanup(phase, result, limits);
                break;
            case ResourceRetirementPhase.RecordOwnerCleanup:
            case ResourceRetirementPhase.RecordOwnerRelease:
            case ResourceRetirementPhase.Finish:
                Source(phase, result, limits);
                break;
            case ResourceRetirementPhase.FinalizeCatalog:
                Finalization(phase, result, limits);
                break;
            case ResourceRetirementPhase.ReleaseOwnerTopology:
                Release(phase, result, limits);
                break;
            case ResourceRetirementPhase.PersistDropParent:
            case ResourceRetirementPhase.CompleteDropParent:
                Parent(phase, result, limits);
                break;
            default:
                throw Invalid();
        }
        ResourceRetirementValidation.Bound(result, limits);
    }

    private static void Page(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementCleanupPageBodyV1>(phase);
        var checkpoint = result.CleanupCheckpoint ?? throw Invalid();
        ResourceRetirementCleanupCheckpointValidation.Require(checkpoint, limits);
        RequireOnly(result, checkpoint: true);
        if (!ResourceRetirementValidation.Same(checkpoint.Control, body.SealedState.Control)
            || !ResourceRetirementValidation.Same(checkpoint.Receiver, body.Receiver) || checkpoint.Partition != body.Partition
            || checkpoint.Ordinal != (body.Previous is null ? ResourceRetirementNativeProtocol.Version
                : checked(body.Previous.Ordinal + ResourceRetirementNativeProtocol.Version))
            || checkpoint.AttemptId != body.Attempt!.AttemptId
            || !ResourceRetirementValidation.Same(checkpoint.Receipt, result.Receipt))
        {
            throw Invalid();
        }
    }

    private static void OwnerCleanup(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementCompleteOwnerCleanupBodyV1>(phase);
        var owner = result.OwnerCleanup ?? throw Invalid();
        RequireOnly(result, cleanup: true);
        ResourceRetirementFinalizationValidation.Owner(owner, body.SealedState, limits);
        if (!ResourceRetirementValidation.Same(owner.Receiver, body.Receiver)
            || !ResourceRetirementValidation.Same(owner.Receipt, result.Receipt))
        {
            throw Invalid();
        }
    }

    private static void Source(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result, DatabaseLimits limits)
    {
        var state = result.State ?? throw Invalid();
        RequireOnly(result, state: true);
        ResourceRetirementStateValidation.State(state, limits);
        var valid = phase.Phase switch
        {
            ResourceRetirementPhase.RecordOwnerCleanup => state.OwnerCleanupReceipts.Any(value =>
                ResourceRetirementValidation.Same(value, Read<ResourceRetirementRecordOwnerCleanupBodyV1>(phase).OwnerCleanup)),
            ResourceRetirementPhase.RecordOwnerRelease => state.OwnerReleaseReceipts.Any(value =>
                ResourceRetirementValidation.Same(value, Read<ResourceRetirementRecordOwnerReleaseBodyV1>(phase).OwnerRelease)),
            ResourceRetirementPhase.Finish => ResourceRetirementValidation.Same(state.CompletionReceipt, result.Receipt),
            _ => false
        };
        if (!valid)
        {
            throw Invalid();
        }
        if (phase.Phase == ResourceRetirementPhase.Finish)
        {
            ResourceRetirementCompletionValidation.RequireCompleted(state, limits);
        }
    }

    private static void Finalization(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result, DatabaseLimits limits)
    {
        var finalization = result.Finalization ?? throw Invalid();
        RequireOnly(result, state: true, finalization: true);
        ResourceRetirementFinalizationValidation.Require(finalization, limits);
        if (result.State is not { } state || !ResourceRetirementValidation.Same(state.Finalization, finalization)
            || !ResourceRetirementValidation.Same(finalization.Control, Read<ResourceRetirementFinalizeCatalogBodyV1>(phase).Control)
            || !ResourceRetirementValidation.Same(finalization.Receipt, result.Receipt))
        {
            throw Invalid();
        }
        ResourceRetirementStateValidation.State(state, limits);
    }

    private static void Release(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementReleaseOwnerTopologyBodyV1>(phase);
        var released = result.OwnerRelease ?? throw Invalid();
        RequireOnly(result, release: true);
        ResourceRetirementCompletionValidation.Release(released, limits);
        if (!ResourceRetirementValidation.Same(released.Finalization, body.Finalization)
            || !ResourceRetirementValidation.Same(released.Receiver, body.Receiver)
            || !ResourceRetirementValidation.Same(released.Receipt, result.Receipt))
        {
            throw Invalid();
        }
    }

    private static void Parent(ResourceRetirementPhaseCommandV1 phase, ResourceRetirementPhaseResultV1 result, DatabaseLimits limits)
    {
        var parent = result.DropParent ?? throw Invalid();
        RequireOnly(result, parent: true);
        var lookup = phase.Phase == ResourceRetirementPhase.PersistDropParent
            ? Read<ResourceRetirementPersistDropParentBodyV1>(phase).Lookup : Read<ResourceRetirementCompleteDropParentBodyV1>(phase).Lookup;
        var receipt = phase.Phase == ResourceRetirementPhase.PersistDropParent ? parent.PersistReceipt : parent.CompleteReceipt;
        if (!ResourceRetirementValidation.Same(parent.Lookup, lookup)
            || !ResourceRetirementValidation.Same(result.Receipt, receipt)
            || phase.Phase == ResourceRetirementPhase.CompleteDropParent && parent.ResultJson is null)
        {
            throw Invalid();
        }
        ResourceRetirementValidation.Bound(parent, limits);
    }

    private static void RequireOnly(ResourceRetirementPhaseResultV1 result, bool state = false, bool checkpoint = false,
        bool cleanup = false, bool finalization = false, bool release = false, bool parent = false)
    {
        if (!state && result.State is not null || !checkpoint && result.CleanupCheckpoint is not null
            || !cleanup && result.OwnerCleanup is not null || !finalization && result.Finalization is not null
            || !release && result.OwnerRelease is not null || !parent && result.DropParent is not null)
        {
            throw Invalid();
        }
    }

    private static T Read<T>(ResourceRetirementPhaseCommandV1 phase) => NativeSerialization.Deserialize<T>(phase.Body.Span);
    private static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
}
