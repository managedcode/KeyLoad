namespace KeyLoad.RecoveryTests.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TextCrashProtocol.ReceiptAlias)]
internal sealed record TextCrashReceipt(
    [property: global::Orleans.Id(0)] Guid NodeId,
    [property: global::Orleans.Id(1)] Guid Incarnation,
    [property: global::Orleans.Id(2)] int DataEpoch,
    [property: global::Orleans.Id(3)] long ReadGeneration,
    [property: global::Orleans.Id(4)] long Position,
    [property: global::Orleans.Id(5)] KeyLoad.PartitionRef Partition,
    [property: global::Orleans.Id(6)] string Collection,
    [property: global::Orleans.Id(7)] string Query,
    [property: global::Orleans.Id(8)] string[] ExpectedIds,
    [property: global::Orleans.Id(9)] TextCrashDocument[] CanonicalDocuments,
    [property: global::Orleans.Id(10)] byte[] PrincipalValue,
    [property: global::Orleans.Id(11)] byte[] ResourceValue,
    [property: global::Orleans.Id(12)] TextCrashFile[] AuthorityFiles,
    [property: global::Orleans.Id(13)] byte[] CredentialSha256,
    [property: global::Orleans.Id(14)] KeyLoad.Query.Features.Search.TextProjectionScope Scope,
    [property: global::Orleans.Id(15)] TextCrashManifestRecord[] ManifestRecords,
    [property: global::Orleans.Id(16)] TextCanonicalState CanonicalState);
