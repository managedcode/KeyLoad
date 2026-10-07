using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Closed native RPC values retained from the existing actual owned log subscription.</summary>
internal sealed record RequestCqrsRpcFailureRecord(Guid WaveId, string Node, Guid RequestId,
    OrleansRpcFailureCategory Category, ErrorCode Error);

internal sealed record RequestCqrsRpcFailureArtifact(int Version, Guid WaveId, RequestCqrsRpcFailureRecord[] Failures);
