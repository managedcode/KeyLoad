using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, MaxDepth = 4,
    PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(RequestCqrsProbeOwnerRecord))]
[JsonSerializable(typeof(RequestCqrsProbeArmRecord))]
[JsonSerializable(typeof(RequestCqrsProbeReleaseRecord))]
[JsonSerializable(typeof(RequestCqrsProbeMarkerRecord))]
internal sealed partial class RequestCqrsProbeJsonContext : JsonSerializerContext
{
}
