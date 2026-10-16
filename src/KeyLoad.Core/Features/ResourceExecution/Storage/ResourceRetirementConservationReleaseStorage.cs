using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Checks actual staged or retained release authority without requiring its not-yet-written outer outcome.</summary>
internal static class ResourceRetirementConservationReleaseStorage
{
    internal static ResourceRetirementConservationReleaseV1? Read(IKeyValueView view, ResourceLifetimeV1 lifetime,
        PhysicalShardRecord owner, DatabaseLimits limits, StorageReadObserver? observer = null)
    {
        long bytes = 0;
        void Observe(long amount)
        {
            if (amount < 0 || amount > limits.MaxQueryReadBytes - bytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementProtocol.Capacity);
            }
            observer?.Invoke(amount);
            bytes = checked(bytes + amount);
        }
        var key = ResourceRetirementConservationKeys.Release(lifetime, owner);
        ResourceRetirementConservationReleaseV1? marker = null;
        view.ReadValue(key, value => marker = ResourceRetirementConservationMarkers.Require(key, value,
            limits.MaxBatchBytes), Observe);
        if (marker is null)
        {
            return null;
        }

        var release = marker.OwnerRelease;
        ResourceRetirementCompletionValidation.Release(release, limits);
        if (!ResourceRetirementValidation.Same(release.Finalization.Control.Lifetime, lifetime)
            || !PhysicalOwnerEntryValidation.SameOwner(release.Receiver.Owner, owner))
        {
            throw Invalid();
        }
        var stored = ResourceRetirementStorage.Read<ResourceRetirementOwnerReleaseV1>(view,
            ResourceRetirementAuthorityKeys.Release(lifetime, owner), limits, Observe);
        if (stored is null || !ResourceRetirementValidation.Same(stored, release))
        {
            throw Invalid();
        }

        var floor = ResourceRetirementConservationRead.ReadFloor(view, limits.MaxBatchBytes, Observe)
            ?? throw Invalid();
        var lineage = ResourceRetirementConservationProvenance.Resolve(view, floor, owner,
            limits.MaxBatchBytes, limits.MaxResults, Observe);
        if (lineage.Revision < marker.After.Revision
            || !lineage.BaselineOriginKey.Span.SequenceEqual(marker.After.BaselineOriginKey.Span))
        {
            throw Invalid();
        }
        var step = ResourceRetirementStorage.Read<ResourceRetirementConservationStepV1>(view,
            ResourceRetirementConservationKeys.Step(owner, marker.After.Revision), limits, Observe)
            ?? throw Invalid();
        ResourceRetirementConservation.ValidateStep(step);
        if (!ResourceRetirementValidation.Same(step.Lifetime, lifetime)
            || step.RetirementId != release.Receipt.RetirementId
            || step.ReleaseCommandId != release.Receipt.CommandId
            || step.AppliedPosition != release.Receipt.AppliedPosition
            || !ResourceRetirementConservation.Same(ResourceRetirementConservation.Advance(marker.Before, step), marker.After))
        {
            throw Invalid();
        }
        RequireRetainedRows(view, release, limits, Observe);
        return marker;
    }

    private static void RequireRetainedRows(IKeyValueView view, ResourceRetirementOwnerReleaseV1 release,
        DatabaseLimits limits, StorageReadObserver observer)
    {
        var control = release.Finalization.Control;
        var owner = release.Receiver.Owner;
        var fence = ResourceRetirementStorage.Read<ResourceRetirementOwnerFenceV1>(view,
            ResourceRetirementStorage.FenceKey(control.Lifetime), limits, observer) ?? throw Invalid();
        ResourceRetirementStateValidation.Fence(fence, limits);
        if (!ResourceRetirementValidation.Same(fence.Control, control)
            || !PhysicalOwnerEntryValidation.SameOwner(fence.Receipt.PhysicalOwner, owner))
        {
            throw Invalid();
        }

        var cleanup = ResourceRetirementStorage.Read<ResourceRetirementOwnerCleanupV1>(view,
            ResourceRetirementAuthorityKeys.Owner(ResourceRetirementAuthorityKeys.CleanupSpace, control.Lifetime, owner),
            limits, observer) ?? throw Invalid();
        var original = release.Finalization.TerminalEvidence.Owners.SingleOrDefault(value =>
            PhysicalOwnerEntryValidation.SameOwner(value.Receiver.Owner, owner));
        if (original is null || !ResourceRetirementValidation.Same(cleanup, original))
        {
            throw Invalid();
        }

        if (PhysicalOwnerEntryValidation.SameOwner(control.Catalog.DefaultShard, owner))
        {
            var history = ResourceRetirementStorage.Read<ResourceRetirementFinalizationV1>(view,
                ResourceRetirementFinalizationKeys.History(control.Lifetime), limits, observer);
            if (history is null || !ResourceRetirementValidation.Same(history, release.Finalization))
            {
                throw Invalid();
            }
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
}
