using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.OrleansExecution;

internal sealed record RequestCqrsClusterMcpRejectionRecord(
    Guid WaveId,
    string Node,
    McpTransportStage Stage,
    McpTransportMethodCategory MethodCategory);
