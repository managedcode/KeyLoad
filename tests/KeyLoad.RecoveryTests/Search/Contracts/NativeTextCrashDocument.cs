namespace KeyLoad.RecoveryTests.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextCrashProtocol.DocumentAlias)]
internal sealed record NativeTextCrashDocument(
    [property: global::Orleans.Id(0)] string Id,
    [property: global::Orleans.Id(1)] byte[] Value);
