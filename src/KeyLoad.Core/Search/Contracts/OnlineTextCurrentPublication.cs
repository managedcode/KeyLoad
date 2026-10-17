namespace KeyLoad.Core.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OnlineTextPublicationProtocol.CurrentPublicationAlias)]
internal sealed record OnlineTextCurrentPublication(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] string PrincipalId,
    [property: global::Orleans.Id(2)] Guid CommandId,
    [property: global::Orleans.Id(3)] string ParentFingerprint,
    [property: global::Orleans.Id(4)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(5)] long ConsumerGeneration,
    [property: global::Orleans.Id(6)] OnlineTextOutcomeAuthority Authority,
    [property: global::Orleans.Id(7)] TextIndexSourceCut PublishedCut);
