using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementPreparationMovementAdmission
{
    internal static void Require(IKeyValueView view, PartitionMovePhaseCommand phase, DatabaseLimits limits)
    {
        var work = new ResourceRetirementMetadataScan(view, limits);
        long indices = ResourceRetirementProtocol.Empty;
        work.Visit(KeyCodec.Encode(ResourceRetirementPreparationKeys.SourceActive), (key, value) =>
        {
            var control = NativeSerialization.Deserialize<ResourceRetirementPreparationControlV1>(value);
            ResourceRetirementPreparationValidation.Control(control, limits);
            var state = ResourceRetirementPreparationStorage.ReadPreparation(view, control.Lifetime, limits, work.Charge);
            if (state is null || state.BegunControl is not null || !ResourceRetirementValidation.Same(state.Control, control))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            ResourceRetirementPreparationEvidence.Source(view, control, limits, work.Charge);
            indices += ResourceRetirementProtocol.First;
            ResourceRetirementMetadataScan.Exact(key, ResourceRetirementPreparationKeys.Life(ResourceRetirementPreparationKeys.SourceActive, control.Lifetime));
            if (!ResourceRetirementPreparationValidation.Contains(control.SourceAcceptedMoves, phase.Partition, phase.MoveId))
            {
                throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Retiring);
            }
            return true;
        });
        work.Visit(KeyCodec.Encode(ResourceRetirementPreparationKeys.OwnerActive), (key, value) =>
        {
            var reservation = NativeSerialization.Deserialize<ResourceRetirementOwnerReservationV1>(value);
            ResourceRetirementPreparationValidation.Reservation(reservation, limits);
            var stored = ResourceRetirementPreparationStorage.ReadReservation(view, reservation.Control.Lifetime, limits, work.Charge);
            if (!ResourceRetirementValidation.Same(stored, reservation))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            ResourceRetirementPreparationEvidence.Owner(view, reservation, limits, work.Charge);
            indices += ResourceRetirementProtocol.First;
            ResourceRetirementMetadataScan.Exact(key, ResourceRetirementPreparationKeys.Life(ResourceRetirementPreparationKeys.OwnerActive, reservation.Control.Lifetime));
            if (!ResourceRetirementPreparationValidation.Accepted(reservation, phase.Partition, phase.MoveId))
            {
                throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Retiring);
            }
            return true;
        });
        if (indices != ResourceRetirementPreparationCounters.Read(view, ResourceRetirementPreparationKeys.Global(), limits))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }
}
