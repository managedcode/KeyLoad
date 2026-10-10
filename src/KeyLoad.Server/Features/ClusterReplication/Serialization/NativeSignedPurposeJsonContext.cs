using System.Text.Json.Serialization;

namespace KeyLoad.Server;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    MaxDepth = NativeDiscoveryOmissionProtocol.JsonDepth, PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(NativeSignedPurposeArm))]
[JsonSerializable(typeof(NativeSignedPurposeWitness))]
internal sealed partial class NativeSignedPurposeJsonContext : JsonSerializerContext
{
}
