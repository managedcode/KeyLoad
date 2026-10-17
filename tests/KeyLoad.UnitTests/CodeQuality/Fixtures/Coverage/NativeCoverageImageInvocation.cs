namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed record NativeCoverageImageInvocation(string Path, string InvocationId, string ContextPath,
    string ServerDirectory, string ServerDllSha256, string ServerPdbSha256, string Mvid,
    string BaseReference, string SourceReceiptPath, string BaseReceiptPath);
