namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnProtocol.OwnedKeyAlias)]
internal sealed record NativeAnnOwnedKey(
    [property: global::Orleans.Id(0)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(1)] long IndexGeneration);
