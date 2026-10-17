using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal readonly record struct RequestCqrsEventVectorExpiryRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] DateTimeOffset OriginalExpiresAt);
