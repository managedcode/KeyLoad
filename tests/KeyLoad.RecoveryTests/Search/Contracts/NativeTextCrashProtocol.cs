namespace KeyLoad.RecoveryTests.Features.Search;

internal static class NativeTextCrashProtocol
{
    internal const string FileAlias = "keyload.crashhost.native-text.file.v1";
    internal const string DocumentAlias = "keyload.crashhost.native-text.document.v1";
    internal const string ManifestRecordAlias = "keyload.crashhost.native-text.manifest-record.v1";
    internal const string ReceiptAlias = "keyload.crashhost.native-text.receipt.v1";
    internal const string CanonicalStateAlias = "keyload.crashhost.native-text.canonical-state.v1";
    internal const int MaximumCanonicalRecords = 4_096;
    internal const long MaximumCanonicalBytes = 1L * 1024 * 1024;
}
