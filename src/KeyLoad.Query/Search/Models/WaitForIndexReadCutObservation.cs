namespace KeyLoad.Query;

/// <summary>Immutable metadata captured from the completed text-index wait's original native view.</summary>
/// <param name="NodeId">Actual physical store owner.</param>
/// <param name="Incarnation">Actual store incarnation.</param>
/// <param name="ReadGeneration">Actual native read generation.</param>
/// <param name="StorePosition">Physical position observed in that same view.</param>
/// <param name="AppliedToken">Exact replication horizon returned by the completed wait.</param>
/// <param name="PrincipalId">Same-view persisted principal.</param>
/// <param name="PolicyEpoch">Same-view persisted policy epoch.</param>
/// <param name="SchemaVersion">Same-view resource schema.</param>
/// <param name="NativeRequestDigest">Digest of the original generated native wait request.</param>
/// <param name="Partition">Original authorized partition.</param>
public readonly record struct WaitForIndexReadCutObservation(Guid NodeId, Guid Incarnation,
    long ReadGeneration, long StorePosition, CommitToken AppliedToken, string PrincipalId,
    long PolicyEpoch, long SchemaVersion, string NativeRequestDigest, PartitionRef Partition);
