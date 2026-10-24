using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.OrleansExecution;

/// <summary>Closed execution RPC values retained from the existing actual owned log subscription.</summary>
internal sealed record RequestCqrsRpcFailureRecord(Guid WaveId, string Node, Guid RequestId,
    OrleansRpcFailureCategory Category, ErrorCode Error, OrleansRpcFailureStage Stage, OrleansRpcFailureKind Kind);
