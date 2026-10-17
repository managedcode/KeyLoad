namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3CoverageImage(string Reference, string ImageId,
    string ContextManifestSha256, string DockerfileSha256, NativeCoverageRf3ArtifactReference MaterializerReceipt,
    NativeCoverageRf3ArtifactReference InspectReceipt);
