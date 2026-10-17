namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed record ReplicaRetainedReplayCommand(ReplicatedOperation Operation, OperationResult Result);
