namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageRf3ServerReceipt(string AssemblyName, string Mvid, string DllSha256,
    string PdbSha256, string SourceReceiptSha256);
