namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal sealed record RequestCqrsClusterMcpRejectionArtifact(
    int Version,
    Guid WaveId,
    RequestCqrsClusterMcpRejectionRecord[] Rejections);
