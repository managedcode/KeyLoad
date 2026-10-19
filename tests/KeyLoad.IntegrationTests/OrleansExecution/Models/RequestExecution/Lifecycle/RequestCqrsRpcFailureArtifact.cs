namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal sealed record RequestCqrsRpcFailureArtifact(int Version, Guid WaveId, RequestCqrsRpcFailureRecord[] Failures);
