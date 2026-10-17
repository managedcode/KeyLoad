using System.Text.Json.Serialization;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record RequestCqrsNativeReplayPlan(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] string PrincipalId,
    [property: JsonRequired] RemoteModelNativeReplayVariation ReplayMode);
