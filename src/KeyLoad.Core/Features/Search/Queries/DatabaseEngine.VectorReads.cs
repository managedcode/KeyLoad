using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Runs an authorized callback over visible vectors at one storage cut.</summary>
    /// <typeparam name="T">Owned callback result type.</typeparam>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Document collection.</param>
    /// <param name="field">Authorized vector field.</param>
    /// <param name="read">Callback over visible document-vector pairs.</param>
    /// <returns>The callback's owned result.</returns>
    public T WithVectors<T>(string principalId, PartitionRef partition, string collection, string field,
        Func<PrincipalRecord, ResourceDefinition, (DocumentRecord Document, VectorRecord Vector)[], T> read) => Store.Read(view =>
    {
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        Authorization.Require(principal, partition, collection, Capability.VectorSearch);
        var resource = Resource(view, partition, collection, ResourceKind.Collection);
        Authorization.RequireFieldUse(principal, resource, field);
        return read(principal, resource, ReadVisibleVectors(view, principal, partition, collection, field));
    });
    /// <summary>Materializes visible revision-matching vector pairs under a shared budget.</summary>
    /// <param name="view">Current gated storage view.</param>
    /// <param name="principal">Persisted principal.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Document collection.</param>
    /// <param name="field">Authorized vector field.</param>
    /// <param name="budget">Shared read budget, or the configured default.</param>
    /// <returns>Owned visible document-vector pairs.</returns>
    public (DocumentRecord Document, VectorRecord Vector)[] ReadVisibleVectors(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, string field, ReadExecutionBudget? budget = null)
    {
        budget ??= new(OperationLimitsOptions, timeProvider: Clock);
        var eligible = new List<(DocumentRecord, VectorRecord)>();
        VisitVisibleVectors(view, principal, partition, collection, field, budget,
            (document, vector) => eligible.Add((document, vector)));
        return eligible.ToArray();
    }

    /// <summary>Visits visible revision-matching vector pairs inside the existing read cut.</summary>
    /// <param name="view">The gate-scoped read view.</param>
    /// <param name="principal">Persisted, verified principal.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Configured collection.</param>
    /// <param name="field">Authorized vector field.</param>
    /// <param name="budget">Shared cancellation and cumulative resource limits.</param>
    /// <param name="visitor">Synchronous callback; it cannot escape or mutate the read view.</param>
    public void VisitVisibleVectors(IKeyValueView view, PrincipalRecord principal, PartitionRef partition,
        string collection, string field, ReadExecutionBudget budget, Action<DocumentRecord, VectorRecord> visitor)
        => VisibleVectorReads.Visit(this, view, principal, partition, collection, field, budget, visitor);
}
