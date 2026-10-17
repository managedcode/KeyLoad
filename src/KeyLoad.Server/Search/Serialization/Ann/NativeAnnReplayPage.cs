namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnProtocol.ReplayPageAlias)]
internal sealed record NativeAnnReplayPage(
    [property: global::Orleans.Id(0)] ProjectionConsumerInfo Consumer,
    [property: global::Orleans.Id(1)] long ThroughSequence,
    [property: global::Orleans.Id(2)] System.Collections.Immutable.ImmutableArray<OutboxEntry> Entries);
