namespace KeyLoad.IntegrationTests.OrleansExecution;

internal sealed record RequestCqrsCaptureArtifactRecord(int Version, Guid WaveId, RequestCqrsCaptureSnapshot[] Nodes);
