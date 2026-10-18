namespace KeyLoad.RecoveryTests.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextCrashProtocol.CanonicalStateAlias)]
internal sealed record NativeTextCanonicalState(
    [property: global::Orleans.Id(0)] int RecordCount,
    [property: global::Orleans.Id(1)] byte[] Sha256);
