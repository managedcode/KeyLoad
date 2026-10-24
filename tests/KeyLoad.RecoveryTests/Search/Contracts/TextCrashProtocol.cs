namespace KeyLoad.RecoveryTests.Search;

internal static class TextCrashProtocol
{
    internal const string FileAlias = "keyload.crashhost.execution-text.file";
    internal const string DocumentAlias = "keyload.crashhost.execution-text.document";
    internal const string ManifestRecordAlias = "keyload.crashhost.execution-text.manifest-record";
    internal const string ReceiptAlias = "keyload.crashhost.execution-text.receipt";
    internal const string CanonicalStateAlias = "keyload.crashhost.execution-text.canonical-state";
    internal const int MaximumCanonicalRecords = 4_096;
    internal const long MaximumCanonicalBytes = 1L * 1024 * 1024;
}
