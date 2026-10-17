namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3NodeReceipt(string Node, string ContainerId, string ImageId,
    string Session, long ServerPid, long ServerStartTicks, NativeCoverageRf3ArtifactReference Terminal,
    NativeCoverageRf3ArtifactReference Coverage, NativeCoverageRf3ArtifactReference ContextManifest);
