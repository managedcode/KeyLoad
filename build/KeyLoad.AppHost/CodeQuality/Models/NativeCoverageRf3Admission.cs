namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed record NativeCoverageRf3Server(string DllPath, string PdbPath, string DllSha256,
    string PdbSha256, string Mvid);

internal sealed record NativeCoverageRf3Admission(string SourceManifestPath, string SourceRevision,
    string SourceManifestSha256, string TestImageManifestSha256, NativeCoverageRf3Server Server,
    IReadOnlyList<NativeCoverageRf3Contributor> Contributors);
