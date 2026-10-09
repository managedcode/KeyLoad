using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal readonly record struct MovementFrameObservationSelection(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid SelectionId,
    [property: JsonRequired] Guid MoveId,
    [property: JsonRequired] RequestCqrsProbePartitionRecord Partition,
    [property: JsonRequired] string OperatorPrincipalId,
    [property: JsonRequired] Guid PhysicalShardId,
    [property: JsonRequired] Guid Incarnation);
