using KeyLoad.Server;

namespace KeyLoad.ClusterTests.OrleansExecution;

internal sealed record RequestCqrsClusterMcpRejectionRecord(
    Guid WaveId,
    string Node,
    McpTransportStage Stage,
    McpTransportMethodCategory MethodCategory);
