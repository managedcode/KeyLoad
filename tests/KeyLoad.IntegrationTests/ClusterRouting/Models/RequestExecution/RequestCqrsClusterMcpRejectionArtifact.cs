namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsClusterMcpRejectionArtifact(
    int Version,
    Guid WaveId,
    RequestCqrsClusterMcpRejectionRecord[] Rejections);
