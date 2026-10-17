namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3FixtureArtifacts(string ContextDirectory, string ContextHash,
    string DockerfileHash, string ImageId, NativeCoverageRf3Server Server,
    NativeCoverageRf3Collector Collector, string BaseImageReference, string BaseReceiptHash);
