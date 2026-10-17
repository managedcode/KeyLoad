using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record RequestCqrsControlledLifetimeStepWitness(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] Guid ConnectionId,
    [property: JsonRequired] int Step,
    [property: JsonRequired] ControlledLifetimeReplayVariation Variation,
    [property: JsonRequired] string PrincipalId,
    [property: JsonRequired] RequestCqrsProbePartitionRecord Partition,
    [property: JsonRequired] Guid EffectCommandId,
    [property: JsonRequired] string TechnicalPrincipalId,
    [property: JsonRequired] string CallerVoter,
    [property: JsonRequired] string CallerSiloAddress,
    [property: JsonRequired] string ReceiverVoter,
    [property: JsonRequired] string ReceiverSiloAddress,
    [property: JsonRequired] int CarrierBytes,
    [property: JsonRequired] string CarrierSha256,
    [property: JsonRequired] int CarrierParts,
    [property: JsonRequired] int RequestBytes,
    [property: JsonRequired] string RequestSha256,
    [property: JsonRequired] int RequestParts,
    [property: JsonRequired] int ReplyBytes,
    [property: JsonRequired] string ReplySha256,
    [property: JsonRequired] int ReplyParts,
    [property: JsonRequired] ErrorCode? Error,
    [property: JsonRequired] Guid? SourceArmId);
