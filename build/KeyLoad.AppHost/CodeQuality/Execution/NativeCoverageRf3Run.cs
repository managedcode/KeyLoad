namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed record NativeCoverageRf3Run(string RunId, string ManifestPath, NativeCoverageRf3Admission Admission,
    string ImageReference);
