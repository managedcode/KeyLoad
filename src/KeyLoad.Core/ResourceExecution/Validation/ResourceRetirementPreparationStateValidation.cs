using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementPreparationStateValidation
{
    internal static void Require(ResourceRetirementPreparationStateV1 state, DatabaseLimits limits)
    {
        if (state is null || state.Reservations.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementPreparationValidation.Control(state.Control, limits);
        if (state.Reservations.Length > state.Control.Directory.Owners.Length || state.Reservations.Length > limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var seen = new HashSet<Guid>();
        foreach (var reservation in state.Reservations)
        {
            ResourceRetirementPreparationValidation.Reservation(reservation, limits);
            if (!ResourceRetirementValidation.Same(reservation.Control, state.Control)
                || !seen.Add(reservation.Receiver.Owner.PhysicalShardId))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
        ResourceRetirementAttemptPointers.Require(state, limits);
        if (state.BegunControl is { } begun)
        {
            ResourceRetirementValidation.Control(begun, limits);
            if (!ResourceRetirementValidation.Same(begun.Request, state.Control.Request)
                || !ResourceRetirementValidation.Same(begun.Lifetime, state.Control.Lifetime)
                || !ResourceRetirementValidation.Same(begun.Catalog, state.Control.Catalog)
                || !ResourceRetirementValidation.Same(begun.Directory, state.Control.Directory))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            AllOwners(state);
        }
        ResourceRetirementValidation.Bound(state, limits);
    }

    internal static void AllOwners(ResourceRetirementPreparationStateV1 state)
    {
        if (state.Reservations.Length != state.Control.Directory.Owners.Length
            || state.Control.Directory.Owners.Any(owner => !state.Reservations.Any(reservation =>
                PhysicalOwnerEntryValidation.Same(owner, reservation.Receiver))))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Incomplete);
        }
    }
}
