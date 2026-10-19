namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProtocol.OwnedKeyAlias)]
internal sealed record AnnOwnedKey(
    [property: global::Orleans.Id(0)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(1)] long IndexGeneration);
