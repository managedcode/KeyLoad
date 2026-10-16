using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPreparationStateV1 RequireRetirementPreparationForBegin(IKeyValueView view,
        ResourceRetirementBeginBodyV1 body)
    {
        if (body.Preparation is null || body.OwnerStatuses.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        var state = RequirePreparationSource(view, body.Preparation);
        ResourceRetirementPreparationStateValidation.AllOwners(state);
        if (state.BegunControl is not null || !ResourceRetirementValidation.Same(body.Request, body.Preparation.Request))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        var nodes = new HashSet<Guid>();
        foreach (var status in body.OwnerStatuses)
        {
            ResourceRetirementPreparationValidation.Status(status, Limits);
            var recorded = state.Reservations.SingleOrDefault(reservation =>
                PhysicalOwnerEntryValidation.Same(reservation.Receiver, status.Reservation.Receiver));
            if (!ResourceRetirementValidation.Same(recorded, status.Reservation) || !status.Remaining.IsEmpty
                || !nodes.Add(status.Cut.NodeId))
            {
                throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Incomplete);
            }
        }
        foreach (var owner in state.Control.Directory.Owners)
        {
            if (body.OwnerStatuses.Count(status => PhysicalOwnerEntryValidation.Same(status.Reservation.Receiver, owner))
                != owner.Owner.VoterIds.Length)
            {
                throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Incomplete);
            }
        }
        var local = body.OwnerStatuses.SingleOrDefault(status => status.Cut.NodeId == Store.Identity.NodeId
            && PhysicalOwnerEntryValidation.SameOwner(status.Reservation.Receiver.Owner, state.Control.Catalog.DefaultShard))
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        var reservation = ResourceRetirementPreparationStorage.ReadReservation(view, state.Control.Lifetime, Limits);
        if (!ResourceRetirementValidation.Same(local.Reservation, reservation)
            || !ResourceRetirementMovementSnapshot.Capture(view, Limits).IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Incomplete);
        }
        ResourceRetirementPreparationEvidence.Owner(view, reservation!, Limits);
        return state;
    }

    private ResourceRetirementOwnerReservationV1 RequireRetirementReservationForFence(IKeyValueView view,
        ResourceRetirementControlV1 control)
    {
        var reservation = ResourceRetirementPreparationStorage.ReadReservation(view, control.Lifetime, Limits)
            ?? throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Missing);
        RequirePreparationReceiver(view, reservation.Control, reservation.Receiver);
        if (!ResourceRetirementValidation.Same(reservation.Control.Request, control.Request)
            || !ResourceRetirementValidation.Same(reservation.Control.Lifetime, control.Lifetime)
            || !ResourceRetirementValidation.Same(reservation.Control.Catalog, control.Catalog)
            || !ResourceRetirementValidation.Same(reservation.Control.Directory, control.Directory)
            || !ResourceRetirementMovementSnapshot.Capture(view, Limits).IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Incomplete);
        }
        ResourceRetirementPreparationEvidence.Owner(view, reservation, Limits);
        return reservation;
    }
}
