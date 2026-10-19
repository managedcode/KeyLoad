namespace KeyLoad.RecoveryTests.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TextCrashProtocol.FileAlias)]
internal sealed record TextCrashFile(
    [property: global::Orleans.Id(0)] string Name,
    [property: global::Orleans.Id(1)] byte[] Sha256);
