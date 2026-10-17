using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record RequestCqrsControlledLifetimeStepAck(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] int Step);
