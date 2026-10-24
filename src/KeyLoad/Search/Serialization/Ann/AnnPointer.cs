namespace KeyLoad.Server.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProtocol.PointerAlias)]
internal sealed record AnnPointer(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] string GenerationLeaf,
    [property: global::Orleans.Id(2)] byte[] ManifestSha256);
