namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterNodeReceipt(string Node, string ContainerId, string ImageId,
    string Session, long ServerPid, long ServerStartTicks, NativeCoverageClusterArtifactReference Terminal,
    NativeCoverageClusterArtifactReference Coverage, NativeCoverageClusterArtifactReference ContextManifest);
