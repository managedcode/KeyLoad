namespace KeyLoad.RecoveryTests.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TextCrashProtocol.CanonicalStateAlias)]
internal sealed record TextCanonicalState(
    [property: global::Orleans.Id(0)] int RecordCount,
    [property: global::Orleans.Id(1)] byte[] Sha256);
