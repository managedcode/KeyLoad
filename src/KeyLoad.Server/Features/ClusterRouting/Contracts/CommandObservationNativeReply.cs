using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(CommandObservationNativeProtocol.ReplyAlias)]
internal sealed record CommandObservationNativeReply(
    [property: global::Orleans.Id(0)] Guid Nonce,
    [property: global::Orleans.Id(1)] string RequestSignature,
    [property: global::Orleans.Id(2)] GrainOperationReply Result,
    [property: global::Orleans.Id(3)] string Signature);
