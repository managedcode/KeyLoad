using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    MaxDepth = MovementFrameObservationProtocol.JsonDepth, PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(MovementFrameObservationOwner))]
[JsonSerializable(typeof(MovementFrameObservationSelection))]
[JsonSerializable(typeof(MovementFrameObservationRecord))]
internal sealed partial class MovementFrameObservationJsonContext : JsonSerializerContext
{
}
