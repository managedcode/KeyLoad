using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record RequestCqrsNativeModelWitness(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] Guid ConnectionId,
    [property: JsonRequired] RequestCqrsProbePhase Phase,
    [property: JsonRequired] string Voter,
    [property: JsonRequired] string SiloAddress,
    [property: JsonRequired] byte[] NativeRequest,
    [property: JsonRequired] byte[]? NativeReply);
