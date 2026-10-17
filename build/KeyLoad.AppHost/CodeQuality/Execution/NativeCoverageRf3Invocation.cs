namespace KeyLoad.AppHost.Features.CodeQuality;

internal sealed record NativeCoverageRf3Invocation(string Path, string ContextDirectory,
    NativeCoverageRf3BaseImageReceipt BaseImage, string ImageReference);
