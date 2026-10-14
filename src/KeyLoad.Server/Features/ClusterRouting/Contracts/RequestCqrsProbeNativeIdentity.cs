using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Original request identity retained across native producer disposal.</summary>
/// <param name="RequestId">Original foreground request identifier.</param>
/// <param name="CommandId">Original command identifier, empty only for reads.</param>
/// <param name="PrincipalId">Original authenticated principal identifier.</param>
/// <param name="ReadKind">Original closed read kind when this is a read.</param>
/// <param name="CommandKind">Original closed command kind when this is a command.</param>
/// <param name="OriginalExpiresAt">Original signed request expiry.</param>
/// <param name="ConnectionId">Original connection owner identifier.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RequestCqrsProbeNativeProtocol.IdentityAlias)]
internal sealed record RequestCqrsProbeNativeIdentity(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] Guid CommandId,
    [property: global::Orleans.Id(2)] string? PrincipalId,
    [property: global::Orleans.Id(3)] GrainReadKind? ReadKind,
    [property: global::Orleans.Id(4)] OperationKind? CommandKind,
    [property: global::Orleans.Id(5)] DateTimeOffset OriginalExpiresAt,
    [property: global::Orleans.Id(6)] Guid ConnectionId);
