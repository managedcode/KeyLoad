namespace KeyLoad.RecoveryHost.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextCrashProtocol.ManifestRecordAlias)]
internal sealed record NativeTextCrashManifestRecord(
    [property: global::Orleans.Id(0)] ulong Index,
    [property: global::Orleans.Id(1)] KeyLoad.EntityRef Reference,
    [property: global::Orleans.Id(2)] long Revision);
