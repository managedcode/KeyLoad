namespace KeyLoad.IntegrationTests.OrleansExecution;

internal sealed record RequestCqrsClusterMcpRejectionArtifact(
    int Version,
    Guid WaveId,
    RequestCqrsClusterMcpRejectionRecord[] Rejections);
