using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementPreparationStatuses
{
    private const long EmptyCut = 0;

    internal static void RequireState(ResourceRetirementPreparationStateV1 state,
        ResourceRetirementRequestV1 request, RegisteredPhysicalOwnerV1 source)
    {
        ResourceRetirementPreparationProof.RequireControl(state.Control, request, source);
        if (state.Reservations.IsDefault || state.Reservations.Length > state.Control.Directory.Owners.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        var owners = new HashSet<Guid>();
        foreach (var reservation in state.Reservations)
        {
            var owner = state.Control.Directory.Owners.FirstOrDefault(value =>
                PhysicalOwnerEntryValidation.SameOwner(value.Owner, reservation.Receiver.Owner));
            if (owner is null || !owners.Add(owner.Owner.PhysicalShardId))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
            ResourceRetirementPreparationProof.RequireReservation(reservation, state.Control, owner);
        }
    }

    internal static void RequireReady(ImmutableArray<ResourceRetirementOwnerStatusV1> statuses,
        ResourceRetirementPreparationStateV1 state)
    {
        if (statuses.IsDefault || statuses.Length != state.Control.Directory.Owners.Sum(value => value.Owner.VoterIds.Length)
            || state.Reservations.Length != state.Control.Directory.Owners.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        var nodes = new HashSet<Guid>();
        foreach (var status in statuses)
        {
            var retained = state.Reservations.SingleOrDefault(value =>
                PhysicalOwnerEntryValidation.SameOwner(value.Receiver.Owner, status.Reservation.Receiver.Owner));
            if (retained is null || !ResourceRetirementReceiptValidation.Same(status.Reservation, retained)
                || status.Cut.NodeId == Guid.Empty || !nodes.Add(status.Cut.NodeId)
                || status.Cut.StoreIncarnation == Guid.Empty || status.Cut.ReadGeneration < EmptyCut
                || status.Cut.StorePosition < EmptyCut || status.Cut.AppliedIndex < EmptyCut || status.Cut.CatalogRevision < EmptyCut
                || status.Remaining.IsDefault || !status.Remaining.IsEmpty)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
            }
        }
        foreach (var owner in state.Control.Directory.Owners)
        {
            if (statuses.Count(value => PhysicalOwnerEntryValidation.SameOwner(value.Reservation.Receiver.Owner, owner.Owner))
                != owner.Owner.VoterIds.Length)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
            }
        }
    }
}
