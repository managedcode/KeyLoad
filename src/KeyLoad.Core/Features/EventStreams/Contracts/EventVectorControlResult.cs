namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorControlResult.SerializerAlias)]
internal sealed record EventVectorControlResult(
    [property: Orleans.Id(EventVectorControlResult.VersionField)] int Version,
    [property: Orleans.Id(EventVectorControlResult.CommandField)] Guid CommandId,
    [property: Orleans.Id(EventVectorControlResult.MapField)] EventVectorMap? Map,
    [property: Orleans.Id(EventVectorControlResult.OfferField)] EventVectorOffer? Offer,
    [property: Orleans.Id(EventVectorControlResult.SourcePhaseField)] EventVectorSourcePhase? SourcePhase,
    [property: Orleans.Id(EventVectorControlResult.ReceiptField)] CommitReceipt Receipt)
{
    internal const string SerializerAlias = "keyload.core.event-vector-control-result.v1";
    internal const int VersionField = 0;
    internal const int CommandField = 1;
    internal const int MapField = 2;
    internal const int OfferField = 3;
    internal const int SourcePhaseField = 4;
    internal const int ReceiptField = 5;
}
