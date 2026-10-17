using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record RequestCqrsControlledLifetimePlan(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string PrincipalId,
    [property: JsonRequired] ControlledLifetimeReplayVariation Variation,
    [property: JsonRequired] Guid? SourceArmId);
