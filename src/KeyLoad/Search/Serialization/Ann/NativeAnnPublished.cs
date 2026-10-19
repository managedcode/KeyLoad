namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnProtocol.PublishedAlias)]
internal sealed record NativeAnnPublished(
    [property: global::Orleans.Id(0)] NativeAnnOwnedKey Key,
    [property: global::Orleans.Id(1)] NativeAnnPointer Pointer);
