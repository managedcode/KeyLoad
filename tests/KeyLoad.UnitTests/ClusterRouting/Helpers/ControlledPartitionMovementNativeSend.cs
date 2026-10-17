using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Uses the retained production sender and its original authenticated native reply.</summary>
internal static class ControlledPartitionMovementNativeSend
{
    internal static Task<PartitionMovementAuthenticatedReply> PhaseAsync(
        PartitionMovementTransportOwner sender, int voterIndex, RegisteredPhysicalOwnerV1 receiver,
        PartitionMovementTransportRequest original, bool local, CancellationToken cancellationToken)
        => sender.ExchangeAsync(voterIndex, receiver, original, NativeSerialization.Serialize(original),
            local, cancellationToken);

    internal static Task<PartitionMovementAuthenticatedReply> OutcomeAsync(
        PartitionMovementTransportOwner sender, int voterIndex, RegisteredPhysicalOwnerV1 receiver,
        PartitionMovementOutcomeTransportRequest original, bool local, CancellationToken cancellationToken)
        => sender.ExchangeOutcomeAsync(voterIndex, receiver, original, NativeSerialization.Serialize(original),
            local, cancellationToken);
}
