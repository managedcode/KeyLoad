using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsRf3McpRejectionRecord(
    Guid WaveId,
    string Node,
    McpTransportStage Stage,
    McpTransportMethodCategory MethodCategory);

internal sealed record RequestCqrsRf3McpRejectionArtifact(
    int Version,
    Guid WaveId,
    RequestCqrsRf3McpRejectionRecord[] Rejections);
