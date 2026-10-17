using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal readonly record struct RequestCqrsProbeActivationRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string Voter,
    [property: JsonRequired] string SiloAddress,
    [property: JsonRequired] string GrainDigest,
    [property: JsonRequired] string ActivationId);
