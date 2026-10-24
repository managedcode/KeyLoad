namespace KeyLoad.IntegrationTests.OrleansExecution;

internal sealed record RequestCqrsRpcFailureArtifact(int Version, Guid WaveId, RequestCqrsRpcFailureRecord[] Failures);
