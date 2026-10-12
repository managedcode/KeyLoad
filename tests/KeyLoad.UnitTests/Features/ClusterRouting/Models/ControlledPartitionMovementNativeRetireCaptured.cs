namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record ControlledPartitionMovementNativeRetireCaptured(int OwnerIndex, ReplicatedOperation Operation);
