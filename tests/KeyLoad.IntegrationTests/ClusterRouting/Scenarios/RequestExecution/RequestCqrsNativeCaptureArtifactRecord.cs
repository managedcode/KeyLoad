namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsNativeCaptureArtifactRecord(int Version, Guid WaveId, RequestCqrsNativeCaptureSnapshot[] Nodes);
