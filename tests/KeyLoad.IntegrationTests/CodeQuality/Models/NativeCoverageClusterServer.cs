namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterServer(string Mvid, string DllSha256, string PdbSha256,
    string SourceReceiptSha256);
