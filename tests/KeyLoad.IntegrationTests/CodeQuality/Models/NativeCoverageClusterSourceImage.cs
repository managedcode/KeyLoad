namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterSourceImage(string Reference, string ManifestDigest,
    string SourceReceiptSha256);
