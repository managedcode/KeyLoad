namespace KeyLoad.Query;

/// <summary>Actual completed scalar query read metadata; not a public operation or persisted authority.</summary>
/// <param name="NodeId">Actual canonical store owner.</param>
/// <param name="Incarnation">Actual canonical incarnation.</param>
/// <param name="ReadGeneration">Actual canonical read generation.</param>
/// <param name="ResultCutPosition">The unchanged returned cursor cut.</param>
/// <param name="StorePosition">Actual physical position under the same read gate.</param>
/// <param name="PrincipalId">Same-view persisted principal.</param>
/// <param name="PolicyEpoch">Same-view persisted policy epoch.</param>
/// <param name="SchemaVersion">Same-view resource schema.</param>
/// <param name="QueryHash">Original normalized query identity.</param>
/// <param name="Partition">Actual query partition.</param>
/// <param name="AccessPath">Original returned access path.</param>
public readonly record struct QueryReadCutObservation(Guid NodeId, Guid Incarnation, long ReadGeneration,
    long ResultCutPosition, long StorePosition, string PrincipalId, long PolicyEpoch, long SchemaVersion,
    string QueryHash, PartitionRef Partition, string AccessPath);
