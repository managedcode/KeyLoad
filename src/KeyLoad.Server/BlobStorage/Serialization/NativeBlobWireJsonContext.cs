using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.BlobStorage;

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(NativeBlobWireOwner))]
[JsonSerializable(typeof(NativeBlobWireArms))]
[JsonSerializable(typeof(NativeBlobWireClaim))]
[JsonSerializable(typeof(NativeBlobWireResult))]
[JsonSerializable(typeof(NativeBlobWireCompletion))]
[JsonSerializable(typeof(NativeBlobWireReservation))]
internal sealed partial class NativeBlobWireJsonContext : JsonSerializerContext;
