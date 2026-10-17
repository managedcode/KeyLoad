using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal readonly record struct RequestCqrsProbeReleaseRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId);
