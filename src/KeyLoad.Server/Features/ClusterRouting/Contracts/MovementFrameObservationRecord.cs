using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal readonly record struct MovementFrameObservationRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid SelectionId,
    [property: JsonRequired] Guid MoveId,
    [property: JsonRequired] RequestCqrsProbePartitionRecord Partition,
    [property: JsonRequired] string OperatorPrincipalId,
    [property: JsonRequired] string ReceiverPrincipalId,
    [property: JsonRequired] Guid PhysicalShardId,
    [property: JsonRequired] Guid Incarnation,
    [property: JsonRequired] string Voter,
    [property: JsonRequired] Guid EffectCommandId,
    [property: JsonRequired] Guid OriginalRequestNonce,
    [property: JsonRequired] DateTimeOffset OriginalExpiresAt,
    [property: JsonRequired] long EntryIndex,
    [property: JsonRequired] long EntryTerm,
    [property: JsonRequired] long RejectedPrefixBytes,
    [property: JsonRequired] int MaximumFrameBytes);
