namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed record NativeCoverageClusterServer(string DllPath, string PdbPath, string DllSha256,
    string PdbSha256, string Mvid);
