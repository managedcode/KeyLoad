namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed record NativeCoverageClusterBaseImageReceipt(string Path, string SourcePath,
    string SourceSha256, string ImageReference);
