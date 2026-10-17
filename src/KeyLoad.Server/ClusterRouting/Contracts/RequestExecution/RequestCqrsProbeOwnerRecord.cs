using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal readonly record struct RequestCqrsProbeOwnerRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] string Voter);
