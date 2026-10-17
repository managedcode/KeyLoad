using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Bounded private native state from one actual signed status caller; never authority.</summary>
internal readonly record struct RequestCqrsReplicaSnapshotRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] long SnapshotIndex,
    [property: JsonRequired] long MaterializedPosition,
    [property: JsonRequired] long CommittedIndex,
    [property: JsonRequired] long LastIndex,
    [property: JsonRequired] bool HasCheckpointPrefix,
    [property: JsonRequired] bool CheckpointPending);
