using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record ResourceRetirementPreparingShapeSeed(ResourceRetirementPreparationValidationState Native,
    ResourceRetirementPhaseCommandV1 Phase, ResourceRetirementPhaseResultV1 Result)
{
    internal ResourceRetirementPreparationControlV1 Control => Native.Preparation.Control;
    internal ResourceRetirementRequestV1 Request => Control.Request;
    internal ControlledPartitionMovementNode Source => Native.Nodes[ResourceRetirementPreparationValidationData.SourceIndex];
    internal ResourceRetirementOwnerReservationV1 Reservation => Native.Preparation.Reservations.Single(reservation =>
        reservation.Receiver.Owner.PhysicalShardId == Source.PhysicalOwner.PhysicalShardId);
    internal ResourceRetirementAttemptContextV1 Context => new(ResourceRetirementAttemptPurpose.OwnerReservation,
        null, Control, Reservation.Receiver);
    internal ResourceRetirementAttemptV1 Attempt => Reservation.Attempt!;
}
