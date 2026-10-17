using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, MaxDepth = 4,
    PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(RequestCqrsControlledLifetimePlan))]
[JsonSerializable(typeof(RequestCqrsControlledLifetimeStepWitness))]
[JsonSerializable(typeof(RequestCqrsControlledLifetimeStepAck))]
internal partial class RequestCqrsControlledLifetimeJsonContext : JsonSerializerContext
{
}
