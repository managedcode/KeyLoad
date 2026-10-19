namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal sealed record RequestCqrsConnectionCancellationArtifact(int Version, Guid WaveId,
    RequestCqrsConnectionCancellationNodeSnapshot[] Nodes);
