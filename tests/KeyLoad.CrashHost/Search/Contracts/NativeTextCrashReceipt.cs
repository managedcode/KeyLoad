namespace KeyLoad.CrashHost.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextCrashProtocol.CanonicalStateAlias)]
internal sealed record NativeTextCanonicalState(
    [property: global::Orleans.Id(0)] int RecordCount,
    [property: global::Orleans.Id(1)] byte[] Sha256);

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

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextCrashProtocol.FileAlias)]
internal sealed record NativeTextCrashFile(
    [property: global::Orleans.Id(0)] string Name,
    [property: global::Orleans.Id(1)] byte[] Sha256);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextCrashProtocol.DocumentAlias)]
internal sealed record NativeTextCrashDocument(
    [property: global::Orleans.Id(0)] string Id,
    [property: global::Orleans.Id(1)] byte[] Value);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextCrashProtocol.ManifestRecordAlias)]
internal sealed record NativeTextCrashManifestRecord(
    [property: global::Orleans.Id(0)] ulong Index,
    [property: global::Orleans.Id(1)] KeyLoad.EntityRef Reference,
    [property: global::Orleans.Id(2)] long Revision);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextCrashProtocol.ReceiptAlias)]
internal sealed record NativeTextCrashReceipt(
    [property: global::Orleans.Id(0)] Guid NodeId,
    [property: global::Orleans.Id(1)] Guid Incarnation,
    [property: global::Orleans.Id(2)] int DataEpoch,
    [property: global::Orleans.Id(3)] long ReadGeneration,
    [property: global::Orleans.Id(4)] long Position,
    [property: global::Orleans.Id(5)] KeyLoad.PartitionRef Partition,
    [property: global::Orleans.Id(6)] string Collection,
    [property: global::Orleans.Id(7)] string Query,
    [property: global::Orleans.Id(8)] string[] ExpectedIds,
    [property: global::Orleans.Id(9)] NativeTextCrashDocument[] CanonicalDocuments,
    [property: global::Orleans.Id(10)] byte[] PrincipalValue,
    [property: global::Orleans.Id(11)] byte[] ResourceValue,
    [property: global::Orleans.Id(12)] NativeTextCrashFile[] AuthorityFiles,
    [property: global::Orleans.Id(13)] byte[] CredentialSha256,
    [property: global::Orleans.Id(14)] KeyLoad.Query.Features.Search.TextProjectionScope Scope,
    [property: global::Orleans.Id(15)] NativeTextCrashManifestRecord[] ManifestRecords,
    [property: global::Orleans.Id(16)] NativeTextCanonicalState CanonicalState);
