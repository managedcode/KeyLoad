namespace KeyLoad.RecoveryHost.Features.Search;

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
