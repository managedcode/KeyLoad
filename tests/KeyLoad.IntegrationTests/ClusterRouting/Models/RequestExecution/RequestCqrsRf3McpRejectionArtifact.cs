namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsRf3McpRejectionArtifact(
    int Version,
    Guid WaveId,
    RequestCqrsRf3McpRejectionRecord[] Rejections);
