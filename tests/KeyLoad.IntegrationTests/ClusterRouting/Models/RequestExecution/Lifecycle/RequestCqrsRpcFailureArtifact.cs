namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsRpcFailureArtifact(int Version, Guid WaveId, RequestCqrsRpcFailureRecord[] Failures);
