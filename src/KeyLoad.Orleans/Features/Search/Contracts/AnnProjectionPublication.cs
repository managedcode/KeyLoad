namespace KeyLoad.Orleans;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProjectionPublicationAlias.Value)]
internal sealed record AnnProjectionPublication(
    [property: global::Orleans.Id(0)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(1)] long IndexGeneration,
    [property: global::Orleans.Id(2)] AnnSourceCut Source,
    [property: global::Orleans.Id(3)] int Count,
    [property: global::Orleans.Id(4)] string IndexSha256);

internal static class AnnProjectionPublicationAlias
{
    internal const string Value = "keyload.ann-projection-publication.v1";
}
