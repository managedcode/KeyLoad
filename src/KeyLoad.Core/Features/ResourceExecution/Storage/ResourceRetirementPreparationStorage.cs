using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementPreparationStorage
{
    internal static ResourceRetirementPreparationStateV1? ReadPreparation(IKeyValueView view,
        ResourceLifetimeV1 lifetime, DatabaseLimits limits, StorageReadObserver? observer = null)
    {
        var state = ResourceRetirementStorage.Read<ResourceRetirementPreparationStateV1>(view,
            ResourceRetirementPreparationKeys.Life(ResourceRetirementPreparationKeys.StateSpace, lifetime), limits, observer);
        if (state is not null)
        {
            ResourceRetirementPreparationStateValidation.Require(state, limits);
            ResourceRetirementAttemptPointers.ValidateStored(view, state, limits, observer);
            SameLife(state.Control.Lifetime, lifetime);
        }
        return state;
    }

    internal static ResourceRetirementOwnerReservationV1? ReadReservation(IKeyValueView view,
        ResourceLifetimeV1 lifetime, DatabaseLimits limits, StorageReadObserver? observer = null)
    {
        var reservation = ResourceRetirementStorage.Read<ResourceRetirementOwnerReservationV1>(view,
            ResourceRetirementPreparationKeys.Life(ResourceRetirementPreparationKeys.OwnerSpace, lifetime), limits, observer);
        if (reservation is not null)
        {
            ResourceRetirementPreparationValidation.Reservation(reservation, limits);
            SameLife(reservation.Control.Lifetime, lifetime);
        }
        return reservation;
    }

    internal static void RequireNoPreparation(IKeyValueView view, DatabaseLimits limits)
        => ResourceRetirementPreparationCounters.RequireZero(view, ResourceRetirementPreparationKeys.Global(), limits);
    internal static void RequireNoPreparation(IKeyValueView view, string tenantId, string databaseId, DatabaseLimits limits)
        => ResourceRetirementPreparationCounters.RequireZero(view, ResourceRetirementPreparationKeys.Database(tenantId, databaseId), limits);

    internal static void RequireOwnerRegistrationOpen(IKeyValueView view, DatabaseLimits limits)
    {
        RequireNoPreparation(view, limits);
        ResourceRetirementStorage.RequireNoRetirement(view, limits);
    }

    internal static void RequireMovementAdmission(IKeyValueView view,
        KeyLoad.Core.Features.ClusterRouting.Contracts.PartitionMovePhaseCommand phase, DatabaseLimits limits)
    {
        ResourceRetirementStorage.RequireNoRetirement(view, limits);
        ResourceRetirementPreparationMovementAdmission.Require(view, phase, limits);
    }

    internal static void CreateSource(IAtomicTransaction transaction, ResourceRetirementPreparationStateV1 state, DatabaseLimits limits)
    {
        if (ReadPreparation(transaction, state.Control.Lifetime, limits) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        WriteState(transaction, state, limits);
        transaction.PutRecord(ResourceRetirementPreparationKeys.Life(ResourceRetirementPreparationKeys.SourceActive, state.Control.Lifetime), state.Control);
        ResourceRetirementPreparationCounters.Add(transaction, state.Control.Lifetime, limits);
    }

    internal static void CreateOwner(IAtomicTransaction transaction, ResourceRetirementOwnerReservationV1 reservation, DatabaseLimits limits)
    {
        ResourceRetirementPreparationValidation.Reservation(reservation, limits);
        if (ReadReservation(transaction, reservation.Control.Lifetime, limits) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        transaction.PutRecord(ResourceRetirementPreparationKeys.Life(ResourceRetirementPreparationKeys.OwnerSpace, reservation.Control.Lifetime), reservation);
        transaction.PutRecord(ResourceRetirementPreparationKeys.Life(ResourceRetirementPreparationKeys.OwnerActive, reservation.Control.Lifetime), reservation);
        ResourceRetirementPreparationCounters.Add(transaction, reservation.Control.Lifetime, limits);
    }

    internal static void WriteState(IAtomicTransaction transaction, ResourceRetirementPreparationStateV1 state, DatabaseLimits limits)
    {
        ResourceRetirementPreparationStateValidation.Require(state, limits);
        transaction.PutRecord(ResourceRetirementPreparationKeys.Life(ResourceRetirementPreparationKeys.StateSpace, state.Control.Lifetime), state);
    }

    internal static void CompleteSource(IAtomicTransaction transaction, ResourceRetirementPreparationStateV1 state,
        ResourceRetirementControlV1 begun, DatabaseLimits limits)
    {
        WriteState(transaction, state with
        {
            BegunControl = begun
        }, limits);
        transaction.Delete(ResourceRetirementPreparationKeys.Life(ResourceRetirementPreparationKeys.SourceActive, state.Control.Lifetime));
        ResourceRetirementPreparationCounters.Remove(transaction, state.Control.Lifetime, limits);
    }

    internal static void CompleteOwner(IAtomicTransaction transaction, ResourceRetirementOwnerReservationV1 reservation, DatabaseLimits limits)
    {
        transaction.Delete(ResourceRetirementPreparationKeys.Life(ResourceRetirementPreparationKeys.OwnerActive, reservation.Control.Lifetime));
        ResourceRetirementPreparationCounters.Remove(transaction, reservation.Control.Lifetime, limits);
    }

    private static void SameLife(ResourceLifetimeV1 actual, ResourceLifetimeV1 expected)
    {
        if (!ResourceRetirementValidation.Same(actual, expected))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }
}
