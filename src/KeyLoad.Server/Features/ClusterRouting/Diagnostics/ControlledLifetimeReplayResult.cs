namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record ControlledLifetimeReplayResult(
    PartitionMovementGrainServiceRequest Request, PartitionMovementGrainServiceReply Reply);
