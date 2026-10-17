namespace KeyLoad;

// One actual owner view; this record never represents a cross-owner atomic snapshot.
/// <summary>Remote graph owner cut.</summary>
/// <param name="Owner">Owner.</param>
/// <param name="Placement">Placement.</param>
/// <param name="NodeId">Node id.</param>
/// <param name="Incarnation">Incarnation.</param>
/// <param name="ReadGeneration">Read generation.</param>
/// <param name="StorePosition">Store position.</param>
/// <param name="AppliedPosition">Applied position.</param>
/// <param name="PolicyEpoch">Policy epoch.</param>
/// <param name="SchemaVersion">Schema version.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphOwnerCutV1.Alias)]
public sealed record RemoteGraphOwnerCutV1(
    [property: global::Orleans.Id(0)] PhysicalShardRecord Owner,
    [property: global::Orleans.Id(1)] AtomicPartitionPlacementResolution Placement,
    [property: global::Orleans.Id(2)] Guid NodeId,
    [property: global::Orleans.Id(3)] Guid Incarnation,
    [property: global::Orleans.Id(4)] long ReadGeneration,
    [property: global::Orleans.Id(5)] long StorePosition,
    [property: global::Orleans.Id(6)] long AppliedPosition,
    [property: global::Orleans.Id(7)] long PolicyEpoch,
    [property: global::Orleans.Id(8)] long SchemaVersion)
{
    internal const string Alias = "keyload.contract.remote-graph-owner-cut.v1";
}
