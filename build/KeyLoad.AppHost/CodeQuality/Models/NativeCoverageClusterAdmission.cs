namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed record NativeCoverageClusterAdmission(string SourceManifestPath, string SourceRevision,
    string SourceManifestSha256, string TestImageManifestSha256, NativeCoverageClusterServer Server,
    IReadOnlyList<NativeCoverageClusterContributor> Contributors);
