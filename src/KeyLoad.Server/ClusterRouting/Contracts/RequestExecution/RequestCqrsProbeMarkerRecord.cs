using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal readonly record struct RequestCqrsProbeMarkerRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] RequestCqrsProbePhase Phase,
    [property: JsonRequired] RequestCqrsProbeOutcome Outcome,
    [property: JsonRequired] string Voter,
    [property: JsonRequired] string SiloAddress,
    [property: JsonRequired] long? EntryIndex = null,
    [property: JsonRequired] long? EntryTerm = null);
