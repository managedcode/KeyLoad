using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record ControlledRetireAspireOriginal(string Node, ReplicatedOperation Operation,
    byte[] SealedBytes, PartitionMoveParentPhase Phase, byte[] PhaseBytes, PartitionMoveResult Expected);
