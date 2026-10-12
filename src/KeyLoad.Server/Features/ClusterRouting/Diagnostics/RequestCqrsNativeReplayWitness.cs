using System.Text.Json.Serialization;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record RequestCqrsNativeReplayWitness(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] Guid ConnectionId,
    [property: JsonRequired] string Voter,
    [property: JsonRequired] string SiloAddress,
    [property: JsonRequired] RemoteModelNativeReplayVariation ReplayMode,
    [property: JsonRequired] int Ordinal,
    [property: JsonRequired] byte[] OriginalNativeRequest,
    [property: JsonRequired] byte[] NativeRequest,
    [property: JsonRequired] byte[] NativeReply);
