namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed record NativeCoverageClusterInvocation(string Path, string ContextDirectory,
    NativeCoverageClusterBaseImageReceipt BaseImage, string ImageReference);
