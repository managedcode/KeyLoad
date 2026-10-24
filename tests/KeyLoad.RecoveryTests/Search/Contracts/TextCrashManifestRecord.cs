namespace KeyLoad.RecoveryTests.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TextCrashProtocol.ManifestRecordAlias)]
internal sealed record TextCrashManifestRecord(
    [property: global::Orleans.Id(0)] ulong Index,
    [property: global::Orleans.Id(1)] KeyLoad.EntityRef Reference,
    [property: global::Orleans.Id(2)] long Revision);
