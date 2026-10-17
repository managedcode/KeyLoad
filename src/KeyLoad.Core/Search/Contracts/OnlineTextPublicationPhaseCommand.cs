namespace KeyLoad.Core.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OnlineTextPublicationProtocol.PhaseCommandAlias)]
internal sealed record OnlineTextPublicationPhaseCommand(
    [property: global::Orleans.Id(0)] OnlineTextIndexMaintenanceRequest Request,
    [property: global::Orleans.Id(1)] OnlineTextIndexMaintenanceResult Result,
    [property: global::Orleans.Id(2)] int DataEpoch,
    [property: global::Orleans.Id(3)] string Leaf,
    [property: global::Orleans.Id(4)] string ManifestSha256,
    [property: global::Orleans.Id(5)] Guid GenerationId,
    [property: global::Orleans.Id(6)] Guid? ExpectedCurrentCommandId,
    [property: global::Orleans.Id(7)] Guid SessionId);
