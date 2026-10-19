namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TextProtocol.FileAlias)]
internal sealed record TextFile(
    [property: global::Orleans.Id(0)] string RelativePath,
    [property: global::Orleans.Id(1)] long Length,
    [property: global::Orleans.Id(2)] byte[] Sha256);
