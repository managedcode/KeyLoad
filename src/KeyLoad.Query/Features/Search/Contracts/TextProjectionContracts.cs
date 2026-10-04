using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

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

/// <summary>Supplies a physical-owner text projection inside the canonical read gate.</summary>
public interface ITextProjection : IDisposable
{
    /// <summary>Admits one bounded build or current generation lease.</summary>
    /// <param name="scope">Source scope captured after persisted authorization.</param>
    /// <param name="budget">Shared operation budget.</param>
    /// <returns>A lease that must be disposed before leaving the same canonical cut.</returns>
    ITextProjectionLease Acquire(TextProjectionScope scope, ReadExecutionBudget budget);
}

/// <summary>Observes canonical tokenization and verifies complete native candidate coverage.</summary>
public interface ITextProjectionLease : IDisposable
{
    /// <summary>Begins the next visible canonical record in storage visitation order.</summary>
    /// <param name="reference">Canonical identity.</param>
    /// <param name="revision">Canonical current revision.</param>
    void BeginRecord(EntityRef reference, long revision);
    /// <summary>Observes one already charged and normalized canonical token.</summary>
    /// <param name="token">The very token supplied to exact ranking.</param>
    void ObserveToken(string token);
    /// <summary>Checks the complete bounded native token union before accepting exact ranks.</summary>
    /// <param name="terms">All distinct exact query terms.</param>
    /// <param name="references">All exact positive identities before Limit or fusion.</param>
    /// <param name="budget">The same shared operation budget.</param>
    void VerifyCandidates(IReadOnlyList<string> terms, IReadOnlyList<EntityRef> references,
        ReadExecutionBudget budget);
}
