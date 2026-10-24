namespace KeyLoad.Server.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProtocol.ReplayPageAlias)]
internal sealed record AnnReplayPage(
    [property: global::Orleans.Id(0)] ProjectionConsumerInfo Consumer,
    [property: global::Orleans.Id(1)] long ThroughSequence,
    [property: global::Orleans.Id(2)] System.Collections.Immutable.ImmutableArray<OutboxEntry> Entries);
