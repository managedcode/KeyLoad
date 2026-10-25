namespace KeyLoad.ClusterTests.OrleansExecution;

internal sealed record RequestCqrsRpcFailureArtifact(int Version, Guid WaveId, RequestCqrsRpcFailureRecord[] Failures);
