namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal sealed record RequestCqrsCaptureArtifactRecord(int Version, Guid WaveId, RequestCqrsCaptureSnapshot[] Nodes);
