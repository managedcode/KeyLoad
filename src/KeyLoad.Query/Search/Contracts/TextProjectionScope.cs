namespace KeyLoad.Query.Search;

/// <summary>Binds a disposable text projection to one authorized canonical storage cut.</summary>
/// <param name="NodeId">Physical source owner.</param>
/// <param name="Incarnation">Canonical authority incarnation.</param>
/// <param name="DataEpoch">Canonical data format epoch.</param>
/// <param name="ReadGeneration">Nonreused canonical replacement generation.</param>
/// <param name="Position">Committed local source position.</param>
/// <param name="Partition">Logical atomic partition.</param>
/// <param name="Collection">Authorized collection.</param>
/// <param name="Field">Exact authorized text field.</param>
/// <param name="PrincipalId">Persisted caller identity.</param>
/// <param name="PolicyEpoch">Persisted caller policy epoch.</param>
/// <param name="SchemaVersion">Collection schema version.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(TextProjectionProtocol.ScopeAlias)]
public sealed record TextProjectionScope(
    [property: Orleans.Id(0)] Guid NodeId,
    [property: Orleans.Id(1)] Guid Incarnation,
    [property: Orleans.Id(2)] int DataEpoch,
    [property: Orleans.Id(3)] long ReadGeneration,
    [property: Orleans.Id(4)] long Position,
    [property: Orleans.Id(5)] PartitionRef Partition,
    [property: Orleans.Id(6)] string Collection,
    [property: Orleans.Id(7)] string Field,
    [property: Orleans.Id(8)] string PrincipalId,
    [property: Orleans.Id(9)] long PolicyEpoch,
    [property: Orleans.Id(10)] long SchemaVersion);
