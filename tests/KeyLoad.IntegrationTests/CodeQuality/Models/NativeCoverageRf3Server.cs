namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3Server(string Mvid, string DllSha256, string PdbSha256,
    string SourceReceiptSha256);
