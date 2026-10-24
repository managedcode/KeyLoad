namespace KeyLoad.IntegrationTests.OrleansExecution;

internal sealed record RequestCqrsConnectionCancellationArtifact(int Version, Guid WaveId,
    RequestCqrsConnectionCancellationNodeSnapshot[] Nodes);
