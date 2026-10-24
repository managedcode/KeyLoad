namespace KeyLoad.Server.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProtocol.PublishedAlias)]
internal sealed record AnnPublished(
    [property: global::Orleans.Id(0)] AnnOwnedKey Key,
    [property: global::Orleans.Id(1)] AnnPointer Pointer);
