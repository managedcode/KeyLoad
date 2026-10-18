namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterCoverageImage(string Reference, string ImageId,
    string ContextManifestSha256, string DockerfileSha256, NativeCoverageClusterArtifactReference MaterializerReceipt,
    NativeCoverageClusterArtifactReference InspectReceipt);
