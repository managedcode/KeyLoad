namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TextProtocol.OwnedPathAlias)]
internal sealed record TextOwnedPath(
    [property: global::Orleans.Id(0)] string RelativePath,
    [property: global::Orleans.Id(1)] bool IsDirectory);
