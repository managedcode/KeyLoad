using System.Text.Json.Serialization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal readonly record struct RequestCqrsQueryReadCutWitness(
    [property: JsonRequired] int Version, [property: JsonRequired] string Kind,
    [property: JsonRequired] string SessionId, [property: JsonRequired] Guid ArmId,
    [property: JsonRequired] Guid RequestId, [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] Guid ConnectionId, [property: JsonRequired] string PrincipalId,
    [property: JsonRequired] string Voter, [property: JsonRequired] string SiloAddress,
    [property: JsonRequired] string GrainDigest, [property: JsonRequired] string ActivationId,
    [property: JsonRequired] Guid NodeId, [property: JsonRequired] Guid Incarnation,
    [property: JsonRequired] long ReadGeneration, [property: JsonRequired] long ResultCutPosition,
    [property: JsonRequired] long StorePosition, [property: JsonRequired] long PolicyEpoch,
    [property: JsonRequired] long SchemaVersion, [property: JsonRequired] string QueryHash,
    [property: JsonRequired] RequestCqrsProbePartitionRecord Partition,
    [property: JsonRequired] string AccessPath);
