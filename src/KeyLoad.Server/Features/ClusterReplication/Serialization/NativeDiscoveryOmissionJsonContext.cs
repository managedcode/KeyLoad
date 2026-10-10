using System.Text.Json.Serialization;

namespace KeyLoad.Server;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    MaxDepth = NativeDiscoveryOmissionProtocol.JsonDepth, PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(NativeDiscoveryOmissionArm))]
[JsonSerializable(typeof(NativeDiscoveryOmissionWitness))]
internal sealed partial class NativeDiscoveryOmissionJsonContext : JsonSerializerContext
{
}
