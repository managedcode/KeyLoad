namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record PartitionMovementLateNativeCaptured(int OwnerIndex, ReplicatedOperation Operation);
