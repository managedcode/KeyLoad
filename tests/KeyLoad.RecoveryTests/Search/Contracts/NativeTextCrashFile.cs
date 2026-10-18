namespace KeyLoad.RecoveryTests.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextCrashProtocol.FileAlias)]
internal sealed record NativeTextCrashFile(
    [property: global::Orleans.Id(0)] string Name,
    [property: global::Orleans.Id(1)] byte[] Sha256);
