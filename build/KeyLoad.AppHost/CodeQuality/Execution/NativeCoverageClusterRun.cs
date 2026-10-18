namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed record NativeCoverageClusterRun(string RunId, string ManifestPath, NativeCoverageClusterAdmission Admission,
    string ImageReference);
