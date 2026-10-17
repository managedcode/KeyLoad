namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeTextProtocol.OwnedPathAlias)]
internal sealed record NativeTextOwnedPath(
    [property: global::Orleans.Id(0)] string RelativePath,
    [property: global::Orleans.Id(1)] bool IsDirectory);
