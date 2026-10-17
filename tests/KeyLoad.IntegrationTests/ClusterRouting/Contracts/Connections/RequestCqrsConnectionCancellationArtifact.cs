namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsConnectionCancellationArtifact(int Version, Guid WaveId,
    RequestCqrsConnectionCancellationNodeSnapshot[] Nodes);
