using System.Text.Json.Serialization;
using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

internal readonly record struct RequestCqrsProbeArmRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] string PrincipalId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] GrainReadKind? ReadKind,
    [property: JsonRequired] RequestCqrsProbePhase Phase,
    [property: JsonRequired] RequestCqrsProbeAction Action,
    [property: JsonRequired] RequestCqrsProbePartitionRecord? Partition = null,
    [property: JsonRequired] Guid? SourceRequestId = null,
    [property: JsonRequired] string? TargetVoter = null,
    [property: JsonRequired] Guid? SourceArmId = null);
