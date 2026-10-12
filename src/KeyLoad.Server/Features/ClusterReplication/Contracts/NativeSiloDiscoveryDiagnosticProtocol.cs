namespace KeyLoad.Server;

internal static class NativeSiloDiscoveryDiagnosticProtocol
{
    internal const string Path = "/api/admin/native-silo-discovery";
    internal const string NonceParameter = "nonce";
    internal const string NonceFormat = "D";
    internal const string SignatureHeader = "X-KeyLoad-Native-Discovery-Signature";
    internal const string ContentType = "application/octet-stream";
    internal const string InvalidRequest = "A single nonempty native discovery nonce is required.";
    internal const string Unavailable = "Native silo discovery is unavailable.";
}
