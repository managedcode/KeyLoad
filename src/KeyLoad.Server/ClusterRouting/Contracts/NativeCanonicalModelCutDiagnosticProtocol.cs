namespace KeyLoad.Server;

internal static class NativeCanonicalModelCutDiagnosticProtocol
{
    internal const string RequestAlias = "keyload.probe.native-canonical-model-cut-request.v1";
    internal const string RowAlias = "keyload.probe.native-canonical-model-cut-row.v1";
    internal const string WitnessAlias = "keyload.probe.native-canonical-model-cut-witness.v1";
    internal const string Path = "/api/admin/native-canonical-model-cut";
    internal const string SignatureHeader = "X-KeyLoad-Native-Canonical-Cut-Signature";
    internal const string ContentType = "application/octet-stream";
    internal const string Unavailable = "The private canonical model cut owner is unavailable.";
}
