namespace KeyLoad.ClusterTests.OrleansExecution;

internal sealed record RequestCqrsConnectionCancellationArtifact(int Version, Guid WaveId,
    RequestCqrsConnectionCancellationNodeSnapshot[] Nodes);
