namespace KeyLoad.Core.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OnlineTextPublicationProtocol.OutcomeAuthorityAlias)]
internal sealed record OnlineTextOutcomeAuthority(
    [property: global::Orleans.Id(0)] string ParentFingerprint,
    [property: global::Orleans.Id(1)] string Collection,
    [property: global::Orleans.Id(2)] string Field,
    [property: global::Orleans.Id(3)] int DataEpoch,
    [property: global::Orleans.Id(4)] PhysicalShardRecord Placement,
    [property: global::Orleans.Id(5)] string Leaf,
    [property: global::Orleans.Id(6)] string ManifestSha256,
    [property: global::Orleans.Id(7)] Guid GenerationId,
    [property: global::Orleans.Id(8)] Guid? ExpectedCurrentCommandId);
