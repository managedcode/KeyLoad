using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record ResourceLifetimeGrantState(
    ControlledPartitionMovementNode Source,
    ControlledPartitionMovementNode Target,
    ServerRuntimeOptions TargetOptions,
    PartitionMovementPeerAdmission TargetAdmission,
    ControlledDocumentNativePhaseFlow Phases,
    ReplicatedOperation Original,
    PartitionControlDocumentCommandContext Context,
    DateTimeOffset Expiry,
    PartitionControlCommandRecord Record,
    PartitionMovePhaseResult Authorization,
    PartitionMovementTransportRequest Request,
    PartitionMoveLifetimeProofV1 Proof,
    long TargetIndex)
{
    internal List<(ReplicatedOperation Original, OperationResult Result)> MetadataAttempts { get; } = [];
}
