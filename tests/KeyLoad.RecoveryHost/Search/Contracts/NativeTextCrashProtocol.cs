namespace KeyLoad.RecoveryHost.Features.Search;

internal static class NativeTextCrashProtocol
{
    internal const string FileAlias = "keyload.recoveryhost.native-text.file.v1";
    internal const string DocumentAlias = "keyload.recoveryhost.native-text.document.v1";
    internal const string ManifestRecordAlias = "keyload.recoveryhost.native-text.manifest-record.v1";
    internal const string ReceiptAlias = "keyload.recoveryhost.native-text.receipt.v1";
    internal const string CanonicalStateAlias = "keyload.recoveryhost.native-text.canonical-state.v1";
    internal const int MaximumCanonicalRecords = 4_096;
    internal const long MaximumCanonicalBytes = 1L * 1024 * 1024;
}
