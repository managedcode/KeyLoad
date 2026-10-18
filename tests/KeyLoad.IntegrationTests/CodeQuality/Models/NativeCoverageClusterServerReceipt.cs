namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal sealed record NativeCoverageClusterServerReceipt(string AssemblyName, string Mvid, string DllSha256,
    string PdbSha256, string SourceReceiptSha256);
