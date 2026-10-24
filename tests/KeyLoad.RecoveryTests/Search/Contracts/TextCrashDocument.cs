namespace KeyLoad.RecoveryTests.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TextCrashProtocol.DocumentAlias)]
internal sealed record TextCrashDocument(
    [property: global::Orleans.Id(0)] string Id,
    [property: global::Orleans.Id(1)] byte[] Value);
